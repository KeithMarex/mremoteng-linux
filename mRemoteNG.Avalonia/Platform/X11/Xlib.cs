using System.Runtime.InteropServices;

namespace mRemoteNG.Avalonia.Platform.X11;

/// <summary>The few libX11 calls needed to manage a window another program puts inside this app.</summary>
internal static class Xlib
{
    private const string Library = "libX11.so.6";

    public const int ButtonPress = 4;
    public const int CreateNotify = 16;
    public const int DestroyNotify = 17;
    public const int MapNotify = 19;
    public const int ReparentNotify = 21;

    public const long ButtonPressMask = 1L << 2;
    public const long SubstructureNotifyMask = 1L << 19;

    public const uint AnyButton = 0;
    public const uint AnyModifier = 1 << 15;
    public const int GrabModeSync = 0;
    public const int GrabModeAsync = 1;
    public const int ReplayPointer = 2;
    public const int RevertToParent = 2;

    [DllImport(Library)]
    public static extern IntPtr XOpenDisplay(IntPtr name);

    [DllImport(Library)]
    public static extern int XCloseDisplay(IntPtr display);

    [DllImport(Library)]
    public static extern int XConnectionNumber(IntPtr display);

    [DllImport(Library)]
    public static extern int XPending(IntPtr display);

    [DllImport(Library)]
    public static extern int XNextEvent(IntPtr display, [Out] IntPtr[] xevent);

    [DllImport(Library)]
    public static extern int XSelectInput(IntPtr display, IntPtr window, IntPtr eventMask);

    [DllImport(Library)]
    public static extern int XQueryTree(IntPtr display, IntPtr window, out IntPtr root, out IntPtr parent,
                                        out IntPtr children, out uint count);

    [DllImport(Library)]
    public static extern int XFree(IntPtr data);

    [DllImport(Library)]
    public static extern int XMoveResizeWindow(IntPtr display, IntPtr window, int x, int y, uint width, uint height);

    [DllImport(Library)]
    public static extern int XSetInputFocus(IntPtr display, IntPtr window, int revertTo, IntPtr time);

    [DllImport(Library)]
    public static extern int XGetInputFocus(IntPtr display, out IntPtr window, out int revertTo);

    [DllImport(Library)]
    public static extern int XGrabButton(IntPtr display, uint button, uint modifiers, IntPtr window, bool ownerEvents,
                                         uint eventMask, int pointerMode, int keyboardMode, IntPtr confineTo, IntPtr cursor);

    [DllImport(Library)]
    public static extern int XAllowEvents(IntPtr display, int eventMode, IntPtr time);

    [DllImport(Library)]
    public static extern int XFlush(IntPtr display);

    /// <summary>The size of an XEvent union in longs (24 on every platform Xlib supports).</summary>
    public const int EventLongs = 24;

    /// <summary>XAnyEvent.type, the first int of every event.</summary>
    public static int EventType(IntPtr[] xevent) => (int)(xevent[0].ToInt64() & 0xFFFFFFFF);

    /// <summary>
    /// The window an event is about: XReparentEvent, XMapEvent, XCreateWindowEvent and XDestroyWindowEvent all
    /// have it after type, serial, send_event, display and event (index 5 in longs).
    /// </summary>
    public static IntPtr EventSubject(IntPtr[] xevent) => xevent[5];

    /// <summary>The window XReparentEvent's window moved to (index 6).</summary>
    public static IntPtr ReparentedTo(IntPtr[] xevent) => xevent[6];

    /// <summary>XButtonEvent.time (type, serial, send_event, display, window, root, subwindow, then time at index 7).</summary>
    public static IntPtr ButtonTime(IntPtr[] xevent) => xevent[7];
}
