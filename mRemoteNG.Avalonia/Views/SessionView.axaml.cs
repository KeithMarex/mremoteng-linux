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
        // Everything else typed in the terminal belongs to the program: stop it before the window's shortcuts see it
        // (Ctrl+Q and Ctrl+S are XON/XOFF to a terminal, not Exit and Save).
        Terminal.AddHandler(KeyDownEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    public event EventHandler<int?>? Exited;

    public Task StartAsync(SessionPlan plan, IReadOnlyDictionary<string, string> environment)
    {
        Terminal.EnvironmentVariables = new Dictionary<string, string>(environment);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Terminal.LaunchProcess(home, plan.Program, [.. plan.Arguments]);
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
