using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TerminalSpike;

public partial class MainWindow : Window
{
    private readonly List<SessionView> _sessions = [];

    public MainWindow()
    {
        InitializeComponent();
        // Tunnel so the shortcuts win over the terminal, which would otherwise send them to the PTY.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        OpenSessionsFromArgs(Program.Args);
    }

    private void OpenSessionsFromArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string? port = i + 2 < args.Length && int.TryParse(args[i + 2], out _) ? args[i + 2] : null;
            switch (args[i])
            {
                case "--ssh" when i + 1 < args.Length:
                    OpenSession("SSH", args[i + 1], port);
                    break;
                case "--telnet" when i + 1 < args.Length:
                    OpenSession("Telnet", args[i + 1], port);
                    break;
                case "--shell":
                    OpenSession("Local shell", "", null);
                    break;
            }
        }
    }

    private void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        string protocol = (ProtocolBox.SelectedItem as ComboBoxItem)?.Content as string ?? "SSH";
        string? port = string.IsNullOrWhiteSpace(PortBox.Text) ? null : PortBox.Text.Trim();
        OpenSession(protocol, HostBox.Text?.Trim() ?? "", port);
    }

    private void OpenSession(string protocol, string target, string? port)
    {
        (string process, string[] args) = protocol switch
        {
            "SSH" => ("ssh", port is null ? [target] : ["-p", port, target]),
            "Telnet" => ("telnet", port is null ? [target] : [target, port]),
            _ => ("bash", Array.Empty<string>()),
        };
        if (protocol != "Local shell" && target.Length == 0)
            return;

        var session = new SessionView(protocol == "Local shell" ? "bash" : $"{protocol}: {target}", process, args);
        _sessions.Add(session);
        Sessions.Children.Add(session);
        Tabs.Items.Add(new TabStripItem { Content = session.Title });
        Tabs.SelectedIndex = _sessions.Count - 1;
    }

    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        for (int i = 0; i < _sessions.Count; i++)
            _sessions[i].IsVisible = i == Tabs.SelectedIndex;
        CurrentSession?.FocusTerminal();
    }

    private SessionView? CurrentSession =>
        Tabs.SelectedIndex >= 0 && Tabs.SelectedIndex < _sessions.Count ? _sessions[Tabs.SelectedIndex] : null;

    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != (KeyModifiers.Control | KeyModifiers.Shift) || CurrentSession is not { } session)
            return;

        switch (e.Key)
        {
            case Key.C:
                e.Handled = true;
                await session.CopyAsync();
                break;
            case Key.V:
                e.Handled = true;
                await session.PasteAsync();
                break;
            case Key.W:
                e.Handled = true;
                CloseSession(session);
                break;
        }
    }

    private void CloseSession(SessionView session)
    {
        int index = _sessions.IndexOf(session);
        session.Kill();
        _sessions.RemoveAt(index);
        Sessions.Children.Remove(session);
        Tabs.Items.RemoveAt(index);
        Tabs.SelectedIndex = Math.Min(index, _sessions.Count - 1);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        foreach (SessionView session in _sessions)
            session.Kill();
        base.OnClosing(e);
    }
}
