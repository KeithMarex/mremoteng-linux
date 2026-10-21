using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Iciclecreek.Terminal;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Avalonia.ViewModels;

namespace mRemoteNG.Avalonia.Views;

/// <summary>A session tab's content: the terminal its program runs in, and its status.</summary>
public partial class SessionView : UserControl, ITerminalHost
{
    private SessionViewModel? _session;

    public SessionView()
    {
        InitializeComponent();
        Terminal.ProcessExited += OnProcessExited;
        // The status bar says why the session ended; the terminal's own "exited with code" line would contradict a disconnect.
        Terminal.WritingMessage += (_, e) => e.Handled |= e.Kind == TerminalMessageKind.ProcessExited;
        // The PTY can only start once the terminal's template is applied.
        Terminal.Loaded += OnTerminalLoaded;
        // Tunnel, so these shortcuts win over the terminal, which would otherwise send them to the program.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnPreviewPointerWheelChanged, RoutingStrategies.Tunnel);
        // Every other chord typed in the terminal belongs to the program: stop it before the window's shortcuts see it
        // (Ctrl+Q and Ctrl+S are XON/XOFF to a terminal, not Exit and Save). A plain key stays unhandled, because only
        // an unhandled key press becomes text input: a character composed from a dead key (' then space) comes as a
        // key press with no key, which the terminal leaves to its text input.
        Terminal.AddHandler(KeyDownEvent, (_, e) => e.Handled |= (e.KeyModifiers & ChordModifiers) != 0,
                            RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private const KeyModifiers ChordModifiers = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta;

    // Ctrl+wheel zooms the terminal by a point per notch, within these sizes.
    internal const double MinFontSize = 6;
    internal const double MaxFontSize = 48;

    // A touchpad scrolls in fractions of a notch; they add up here until they make a whole one.
    private double _zoomNotches;

    public event EventHandler<int?>? Exited;

    public Task StartAsync(SessionPlan plan, IReadOnlyDictionary<string, string> environment)
    {
        Terminal.EnvironmentVariables = new Dictionary<string, string>(environment);
        string directory = plan.WorkingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Terminal.LaunchProcess(directory, plan.Program, [.. plan.Arguments]);
    }

    public void Kill()
    {
        if (Terminal.IsLive)
            Terminal.Kill();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_session is not null)
            _session.PropertyChanged -= OnSessionPropertyChanged;
        _session = DataContext as SessionViewModel;
        if (_session is not null)
            _session.PropertyChanged += OnSessionPropertyChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Removed with its tab.
        Kill();
        base.OnDetachedFromVisualTree(e);
    }

    private async void OnTerminalLoaded(object? sender, RoutedEventArgs e)
    {
        if (_session is null)
            return;
        await _session.AttachAsync(this);
        Terminal.Focus();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionViewModel.IsSelected) && _session?.IsSelected == true)
            Dispatcher.UIThread.Post(() => Terminal.Focus());
    }

    private void OnProcessExited(object? sender, ProcessExitedEventArgs e)
    {
        int? exitCode = e.ExitCodeKnown ? e.ExitCode : null;
        Dispatcher.UIThread.Post(() => Exited?.Invoke(this, exitCode));
    }

    // Ahead of the terminal, which would scroll its scrollback or send the wheel to a program that follows the mouse.
    // The terminal fits its rows and columns to the new size and tells the program.
    private void OnPreviewPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control)
            return;

        e.Handled = true;
        _zoomNotches += e.Delta.Y;
        int notches = (int)_zoomNotches;
        if (notches == 0)
            return;
        _zoomNotches -= notches;
        Terminal.FontSize = Math.Clamp(Terminal.FontSize + notches, MinFontSize, MaxFontSize);
    }

    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != (KeyModifiers.Control | KeyModifiers.Shift))
            return;

        switch (e.Key)
        {
            case Key.C:
                e.Handled = true;
                await Terminal.CopyAsync();
                break;
            case Key.V:
                e.Handled = true;
                await Terminal.PasteAsync();
                break;
            case Key.W:
                e.Handled = true;
                _session?.CloseCommand.Execute(null);
                break;
        }
    }
}
