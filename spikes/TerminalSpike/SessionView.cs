using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Iciclecreek.Terminal;

namespace TerminalSpike;

/// <summary>One connection tab: a PTY-backed terminal plus a status bar with a reconnect button.</summary>
public sealed class SessionView : DockPanel
{
    private readonly TerminalControl _terminal;
    private readonly TextBlock _status;
    private readonly Button _reconnect;
    private readonly string _process;
    private readonly string[] _args;
    private bool _launched;

    public string Title { get; }

    public SessionView(string title, string process, string[] args)
    {
        Title = title;
        _process = process;
        _args = args;

        _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) };
        _reconnect = new Button { Content = "Reconnect", IsVisible = false, Margin = new Thickness(4) };
        _reconnect.Click += (_, _) => Launch();

        var statusBar = new DockPanel();
        SetDock(_reconnect, Dock.Right);
        statusBar.Children.Add(_reconnect);
        statusBar.Children.Add(_status);
        SetDock(statusBar, Dock.Bottom);

        _terminal = new TerminalControl { FontSize = 14, BufferSize = 5000 };
        _terminal.ProcessExited += OnProcessExited;
        // The PTY can only start once the control's template is applied.
        _terminal.Loaded += (_, _) =>
        {
            if (!_launched)
                Launch();
        };

        Children.Add(statusBar);
        Children.Add(_terminal);
    }

    private void Launch()
    {
        _launched = true;
        _reconnect.IsVisible = false;
        _status.Text = string.Join(' ', [_process, .. _args]);
        _terminal.LaunchProcess(null, _process, _args);
        FocusTerminal();
    }

    private void OnProcessExited(object? sender, ProcessExitedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _status.Text = $"Session ended (exit code {e.ExitCode})";
            _reconnect.IsVisible = true;
        });
    }

    public void FocusTerminal() => _terminal.Focus();

    public Task CopyAsync() => _terminal.CopyAsync();

    public Task PasteAsync() => _terminal.PasteAsync();

    public void Kill()
    {
        if (_terminal.IsLive)
            _terminal.Kill();
    }
}
