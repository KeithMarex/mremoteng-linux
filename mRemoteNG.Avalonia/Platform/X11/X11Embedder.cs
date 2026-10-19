using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace mRemoteNG.Avalonia.Platform.X11;

/// <summary>
/// Looks after the window another program (xfreerdp) puts inside a window of this app: keeps it the size of its host,
/// and gives it the keyboard when it is clicked or shown. An embedded window does not get keyboard focus from the
/// window manager, so a click on it is caught (a passive button grab, as window managers do it), focus is moved to it,
/// and the click is passed on. Xlib runs on a worker thread with a connection of its own.
/// </summary>
internal sealed class X11Embedder : IDisposable
{
    private static readonly int PollMilliseconds = 20;

    private readonly IntPtr _display;
    private readonly IntPtr _host;
    private readonly ConcurrentQueue<Action> _actions = new();
    private readonly Thread _thread;
    private volatile bool _stopped;
    private IntPtr _child;
    private uint _width = 1;
    private uint _height = 1;

    private X11Embedder(IntPtr display, IntPtr host)
    {
        _display = display;
        _host = host;
        Xlib.XSelectInput(display, host, new IntPtr(Xlib.SubstructureNotifyMask));
        Xlib.XFlush(display);
        _thread = new Thread(Run) { IsBackground = true, Name = "X11 embedding" };
        _thread.Start();
    }

    /// <summary>Starts looking after <paramref name="host"/>'s child window; null when the X server cannot be reached.</summary>
    public static X11Embedder? TryCreate(IntPtr host)
    {
        try
        {
            IntPtr display = Xlib.XOpenDisplay(IntPtr.Zero);
            return display == IntPtr.Zero ? null : new X11Embedder(display, host);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Gives the embedded window this size, now and when it appears.</summary>
    public void Resize(int width, int height) => Post(() =>
    {
        _width = (uint)Math.Max(width, 1);
        _height = (uint)Math.Max(height, 1);
        if (_child != IntPtr.Zero)
            Xlib.XMoveResizeWindow(_display, _child, 0, 0, _width, _height);
    });

    /// <summary>Gives the embedded window the keyboard.</summary>
    public void Focus() => Post(() =>
    {
        if (_child != IntPtr.Zero)
            Xlib.XSetInputFocus(_display, _child, Xlib.RevertToParent, IntPtr.Zero);
    });

    /// <summary>Gives the keyboard back to <paramref name="window"/> (this app's window) if the embedded window has it.</summary>
    public void ReturnFocus(IntPtr window) => Post(() =>
    {
        if (_child == IntPtr.Zero)
            return;
        Xlib.XGetInputFocus(_display, out IntPtr focused, out _);
        if (focused == _child)
            Xlib.XSetInputFocus(_display, window, Xlib.RevertToParent, IntPtr.Zero);
    });

    public void Dispose()
    {
        _stopped = true;
        if (Thread.CurrentThread != _thread)
            _thread.Join(TimeSpan.FromSeconds(1));
    }

    private void Post(Action action)
    {
        if (!_stopped)
            _actions.Enqueue(action);
    }

    private void Run()
    {
        IntPtr[] xevent = new IntPtr[Xlib.EventLongs];
        PollDescriptor connection = new() { Descriptor = Xlib.XConnectionNumber(_display), Events = PollIn };
        try
        {
            while (!_stopped)
            {
                while (_actions.TryDequeue(out Action? action))
                    action();
                while (Xlib.XPending(_display) > 0)
                {
                    Xlib.XNextEvent(_display, xevent);
                    Handle(xevent);
                }

                Xlib.XFlush(_display);
                connection.Returned = 0;
                _ = poll(ref connection, 1, PollMilliseconds);
            }
        }
        finally
        {
            Xlib.XCloseDisplay(_display);
        }
    }

    private void Handle(IntPtr[] xevent)
    {
        IntPtr window = Xlib.EventSubject(xevent);
        switch (Xlib.EventType(xevent))
        {
            case Xlib.ReparentNotify when Xlib.ReparentedTo(xevent) == _host:
            case Xlib.CreateNotify when xevent[4] == _host:
                Adopt(window);
                break;
            case Xlib.ReparentNotify when window == _child:
            case Xlib.DestroyNotify when window == _child:
                _child = IntPtr.Zero;
                break;
            case Xlib.MapNotify when window == _child:
                // A new session gets the keyboard once its window can take it.
                Xlib.XMoveResizeWindow(_display, _child, 0, 0, _width, _height);
                Xlib.XSetInputFocus(_display, _child, Xlib.RevertToParent, IntPtr.Zero);
                break;
            case Xlib.ButtonPress:
                // Our grab caught a click on the embedded window: focus it, then let the click through.
                if (_child != IntPtr.Zero)
                    Xlib.XSetInputFocus(_display, _child, Xlib.RevertToParent, Xlib.ButtonTime(xevent));
                Xlib.XAllowEvents(_display, Xlib.ReplayPointer, Xlib.ButtonTime(xevent));
                break;
        }
    }

    private void Adopt(IntPtr window)
    {
        _child = window;
        Xlib.XGrabButton(_display, Xlib.AnyButton, Xlib.AnyModifier, window, false, (uint)Xlib.ButtonPressMask,
                         Xlib.GrabModeSync, Xlib.GrabModeAsync, IntPtr.Zero, IntPtr.Zero);
        Xlib.XMoveResizeWindow(_display, window, 0, 0, _width, _height);
    }

    private const short PollIn = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PollDescriptor
    {
        public int Descriptor;
        public short Events;
        public short Returned;
    }

    [DllImport("libc")]
    private static extern int poll(ref PollDescriptor descriptors, nuint count, int timeout);
}
