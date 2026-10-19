using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using mRemoteNG.Avalonia.Platform.X11;

namespace mRemoteNG.Avalonia.Views;

/// <summary>
/// An X11 window inside the app that another program (xfreerdp) can put its own window in. The embedded window is kept
/// the size of this control, and gets the keyboard when it is clicked; a click anywhere else in the app takes the
/// keyboard back.
/// </summary>
public sealed class X11EmbedHost : NativeControlHost
{
    private X11Embedder? _embedder;
    private TopLevel? _topLevel;

    /// <summary>The X11 window to embed in; null until the control is shown, or when the app does not run on X11.</summary>
    public ulong? WindowId { get; private set; }

    /// <summary>Raised when <see cref="WindowId"/> has been created.</summary>
    public event EventHandler? WindowCreated;

    /// <summary>The control's size in physical pixels.</summary>
    public PixelSize PixelSize
    {
        get
        {
            double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            return PixelSize.FromSize(Bounds.Size, scaling);
        }
    }

    /// <summary>The size of the screen the app is on, in physical pixels.</summary>
    public PixelSize ScreenSize
    {
        get
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            Screen? screen = topLevel?.Screens?.ScreenFromVisual(this) ?? topLevel?.Screens?.Primary;
            return screen?.Bounds.Size ?? new PixelSize(1920, 1080);
        }
    }

    /// <summary>Gives the embedded window the keyboard.</summary>
    public void FocusEmbedded() => _embedder?.Focus();

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        IPlatformHandle handle = base.CreateNativeControlCore(parent);
        if (handle.HandleDescriptor == "XID")
        {
            _embedder = X11Embedder.TryCreate(handle.Handle);
            if (_embedder is not null)
            {
                WindowId = (ulong)handle.Handle;
                PixelSize size = PixelSize;
                _embedder.Resize(size.Width, size.Height);
                WindowCreated?.Invoke(this, EventArgs.Empty);
            }
        }

        return handle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _embedder?.Dispose();
        _embedder = null;
        WindowId = null;
        base.DestroyNativeControlCore(control);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        PixelSize size = PixelSize;
        _embedder?.Resize(size.Width, size.Height);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(PointerPressedEvent, OnAppPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _topLevel?.RemoveHandler(PointerPressedEvent, OnAppPointerPressed);
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    // Clicks on the embedded window go to its program, so a click the app sees is somewhere else in the app.
    private void OnAppPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_topLevel?.TryGetPlatformHandle() is { HandleDescriptor: "XID" } window)
            _embedder?.ReturnFocus(window.Handle);
    }
}
