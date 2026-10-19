using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using System.Runtime.InteropServices;
using mRemoteNG.Connection.Protocol.VNC;
using mRemoteNG.Vnc;

namespace mRemoteNG.Avalonia.Views;

/// <summary>
/// Shows a VNC session's screen and sends it the keyboard and mouse: the counterpart of VncSharp's RemoteDesktop
/// control on Windows. The screen is fitted as the connection's Smart Size setting says.
/// </summary>
public sealed class VncDisplay : Control
{
    public static readonly StyledProperty<RfbClient?> ClientProperty = AvaloniaProperty.Register<VncDisplay, RfbClient?>(nameof(Client));

    public static readonly StyledProperty<SmartSizeMode> SmartSizeProperty =
        AvaloniaProperty.Register<VncDisplay, SmartSizeMode>(nameof(SmartSize), SmartSizeMode.SmartSAspect);

    public static readonly StyledProperty<bool> ViewOnlyProperty = AvaloniaProperty.Register<VncDisplay, bool>(nameof(ViewOnly));

    private readonly Dictionary<Key, uint> _pressed = [];
    private WriteableBitmap? _bitmap;
    private Rect _screen;
    private byte _buttons;
    private int _refreshQueued;
    private string? _clipboard;

    static VncDisplay()
    {
        FocusableProperty.OverrideDefaultValue<VncDisplay>(true);
        AffectsMeasure<VncDisplay>(ClientProperty, SmartSizeProperty);
        AffectsRender<VncDisplay>(SmartSizeProperty);
    }

    public RfbClient? Client
    {
        get => GetValue(ClientProperty);
        set => SetValue(ClientProperty, value);
    }

    public SmartSizeMode SmartSize
    {
        get => GetValue(SmartSizeProperty);
        set => SetValue(SmartSizeProperty, value);
    }

    public bool ViewOnly
    {
        get => GetValue(ViewOnlyProperty);
        set => SetValue(ViewOnlyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ClientProperty)
            return;
        if (change.OldValue is RfbClient old)
            Detach(old);
        if (change.NewValue is RfbClient client)
            Attach(client);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_bitmap is null)
            return default;
        // Without smart sizing the screen shows pixel for pixel (and the tab scrolls).
        Size actual = _bitmap.PixelSize.ToSize(Scaling);
        return SmartSize == SmartSizeMode.SmartSNo || double.IsInfinity(availableSize.Width) || double.IsInfinity(availableSize.Height)
            ? actual
            : availableSize;
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        if (_bitmap is null)
            return;

        Size image = _bitmap.PixelSize.ToSize(Scaling);
        _screen = SmartSize switch
        {
            SmartSizeMode.SmartSFree => new Rect(Bounds.Size),
            SmartSizeMode.SmartSAspect => Fit(image, Bounds.Size),
            _ => new Rect(image)
        };
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            context.DrawImage(_bitmap, new Rect(_bitmap.Size), _screen);
    }

    private static Rect Fit(Size image, Size space)
    {
        double scale = Math.Min(space.Width / image.Width, space.Height / image.Height);
        Size size = image * scale;
        return new Rect(new Point((space.Width - size.Width) / 2, (space.Height - size.Height) / 2), size);
    }

    private double Scaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

    private void Attach(RfbClient client)
    {
        client.Updated += OnUpdated;
        client.Resized += OnResized;
        client.CursorChanged += OnCursorChanged;
        client.ClipboardChanged += OnClipboardChanged;
        CreateBitmap(client.Framebuffer);
        CopyScreen();
    }

    private void Detach(RfbClient client)
    {
        client.Updated -= OnUpdated;
        client.Resized -= OnResized;
        client.CursorChanged -= OnCursorChanged;
        client.ClipboardChanged -= OnClipboardChanged;
        _pressed.Clear();
        _buttons = 0;
        Cursor = null;
    }

    private void CreateBitmap(Framebuffer framebuffer)
    {
        _bitmap?.Dispose();
        // The framebuffer's fourth byte is padding, so the bitmap is opaque.
        _bitmap = new WriteableBitmap(new PixelSize(framebuffer.Width, framebuffer.Height), new Vector(96, 96),
                                      PixelFormats.Bgra8888, AlphaFormat.Opaque);
        InvalidateMeasure();
    }

    // Updates arrive on the session's thread; the screen is copied on the UI thread, at most once per frame.
    private void OnUpdated(ScreenRect changed)
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 0)
            Dispatcher.UIThread.Post(CopyScreen, DispatcherPriority.Render);
    }

    private void OnResized() => Dispatcher.UIThread.Post(() =>
    {
        if (Client is { } client)
            CreateBitmap(client.Framebuffer);
    });

    private void CopyScreen()
    {
        Interlocked.Exchange(ref _refreshQueued, 0);
        if (Client?.Framebuffer is not { } framebuffer || _bitmap is null || _bitmap.PixelSize.Width != framebuffer.Width ||
            _bitmap.PixelSize.Height != framebuffer.Height)
        {
            return;
        }

        using (ILockedFramebuffer target = _bitmap.Lock())
        {
            lock (framebuffer.SyncRoot)
                CopyRows(framebuffer.Pixels, framebuffer.Width, framebuffer.Height, target);
        }

        InvalidateVisual();
    }

    // 32-bit pixels into a bitmap's rows (uint[] and int[] share their layout, which Marshal.Copy needs).
    private static void CopyRows(uint[] pixels, int width, int height, ILockedFramebuffer target)
    {
        int[] source = (int[])(object)pixels;
        for (int y = 0; y < height; y++)
            Marshal.Copy(source, y * width, target.Address + y * target.RowBytes, width);
    }

    private void OnCursorChanged(CursorShape shape) => Dispatcher.UIThread.Post(() =>
    {
        using WriteableBitmap image = new(new PixelSize(shape.Width, shape.Height), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Unpremul);
        using (ILockedFramebuffer target = image.Lock())
            CopyRows(shape.Pixels, shape.Width, shape.Height, target);

        Cursor = new Cursor(image, new PixelPoint(shape.HotX, shape.HotY));
    });

    private void OnClipboardChanged(string text) => Dispatcher.UIThread.Post(async () =>
    {
        _clipboard = text;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    });

    // The local clipboard goes to the remote computer when the session gets the keyboard, as VncSharp does.
    protected override async void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (Client is not { } client || ViewOnly || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;
        string? text = await clipboard.TryGetTextAsync();
        if (text is not null && text != _clipboard)
        {
            _clipboard = text;
            client.SendClipboard(text);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Client is not { } client || ViewOnly)
        {
            e.Handled = true;
            return;
        }

        // A key that types a character comes back as text (OnTextInput), which Avalonia only sends for a key press
        // left unhandled; a shortcut such as Ctrl+C types no text, so it goes as the key. Handled keys also keep the
        // window's menu shortcuts (Ctrl+Q, Ctrl+S, …) from firing.
        bool shortcut = _pressed.Keys.Any(VncKeyMap.ShortcutModifiers.Contains);
        uint? keysym = VncKeyMap.SpecialKeySym(e.Key) ?? (shortcut ? VncKeyMap.ShortcutKeySym(e.Key, e.KeySymbol) : null);
        if (keysym is null)
            return;
        e.Handled = true;
        _pressed[e.Key] = keysym.Value;
        client.SendKey(keysym.Value, down: true);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        e.Handled = true;
        // A key goes up as the keysym it went down as, whatever the modifiers do in between.
        if (Client is { } client && _pressed.Remove(e.Key, out uint keysym))
            client.SendKey(keysym, down: false);
    }

    // Typed characters, after the keyboard layout, Shift and dead keys: each is pressed and released at once.
    protected override void OnTextInput(TextInputEventArgs e)
    {
        e.Handled = true;
        if (Client is not { } client || ViewOnly || string.IsNullOrEmpty(e.Text))
            return;
        foreach (System.Text.Rune character in e.Text.EnumerateRunes())
        {
            if (System.Text.Rune.IsControl(character))
                continue;
            uint keysym = VncKeyMap.FromCodePoint(character.Value);
            client.SendKey(keysym, down: true);
            client.SendKey(keysym, down: false);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        SendPointer(e);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        SendPointer(e);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e) => SendPointer(e);

    // The wheel is buttons 4 to 7, pressed and released at once.
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        byte wheel = e.Delta.Y > 0 ? (byte)8 : e.Delta.Y < 0 ? (byte)16 : e.Delta.X < 0 ? (byte)32 : e.Delta.X > 0 ? (byte)64 : (byte)0;
        if (wheel != 0 && Position(e) is { } position && Client is { } client && !ViewOnly)
        {
            client.SendPointer(position.X, position.Y, (byte)(_buttons | wheel));
            client.SendPointer(position.X, position.Y, _buttons);
        }

        e.Handled = true;
    }

    private void SendPointer(PointerEventArgs e)
    {
        if (Client is not { } client || ViewOnly || Position(e) is not { } position)
            return;
        PointerPointProperties pointer = e.GetCurrentPoint(this).Properties;
        _buttons = (byte)((pointer.IsLeftButtonPressed ? 1 : 0) | (pointer.IsMiddleButtonPressed ? 2 : 0) | (pointer.IsRightButtonPressed ? 4 : 0));
        client.SendPointer(position.X, position.Y, _buttons);
    }

    // From the control to the remote screen's pixels.
    private PixelPoint? Position(PointerEventArgs e)
    {
        if (_bitmap is null || _screen.Width <= 0 || _screen.Height <= 0)
            return null;
        Point point = e.GetPosition(this);
        int x = (int)((point.X - _screen.X) * _bitmap.PixelSize.Width / _screen.Width);
        int y = (int)((point.Y - _screen.Y) * _bitmap.PixelSize.Height / _screen.Height);
        return new PixelPoint(Math.Clamp(x, 0, _bitmap.PixelSize.Width - 1), Math.Clamp(y, 0, _bitmap.PixelSize.Height - 1));
    }
}
