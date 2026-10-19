using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Messages;

namespace mRemoteNG.Avalonia.ViewModels;

public enum SessionState
{
    NotStarted,
    Running,
    Ended,
    Failed
}

/// <summary>
/// A terminal session tab. It starts once its terminal is shown, and is listed in the connection's OpenConnections while it
/// runs, as the WinForms app does with its protocols. A session that ends cleanly closes its tab (like PuTTY's
/// default); otherwise, or when it was disconnected, the tab stays, with the reason and a Reconnect button.
/// </summary>
public sealed partial class SessionViewModel : SessionTabViewModel, IProtocolSession
{
    private readonly SessionLauncher _launcher;
    private readonly SessionPlan? _plan;
    private ITerminalHost? _host;
    private PreparedSession? _prepared;
    private bool _disconnecting;

    /// <param name="plan">What to run instead of the connection's protocol, such as an external tool.</param>
    public SessionViewModel(ConnectionInfo connection, SessionLauncher launcher, SessionPlan? plan = null) : base(connection)
    {
        _launcher = launcher;
        _plan = plan;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive), nameof(HasEnded))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectCommand))]
    private SessionState _state;

    public override bool IsLive => State == SessionState.Running;

    public override bool HasEnded => State is SessionState.Ended or SessionState.Failed;

    /// <summary>Called by the view once its terminal can run a program; starts the session the first time.</summary>
    public async Task AttachAsync(ITerminalHost host)
    {
        if (_host is not null)
            _host.Exited -= OnExited;
        _host = host;
        host.Exited += OnExited;
        if (State == SessionState.NotStarted)
            await ConnectAsync();
    }

    /// <summary>Ends the session (IProtocolSession); the tab stays open.</summary>
    public void Disconnect()
    {
        if (!IsLive || _host is null)
            return;
        _disconnecting = true;
        _host.Kill();
    }

    // (HasEnded itself is declared twice for the toolkit: abstract and override.)
    private bool CanReconnect => HasEnded;

    [RelayCommand(CanExecute = nameof(CanReconnect))]
    private Task ReconnectAsync() => ConnectAsync();

    protected override void EndSession() => Disconnect();

    protected override void OnDisposed()
    {
        if (_host is not null)
            _host.Exited -= OnExited;
        Connection.OpenConnections.Remove(this);
        _prepared?.Dispose();
    }

    private async Task ConnectAsync()
    {
        if (_host is null || IsLive || IsDisposed)
            return;

        _prepared?.Dispose();
        _prepared = null;
        PreparedSession prepared;
        try
        {
            prepared = _plan is null ? _launcher.Prepare(Connection) : _launcher.Prepare(_plan);
        }
        catch (SessionException ex)
        {
            Fail(ex.Message);
            return;
        }

        foreach (string warning in prepared.Warnings)
            CoreRuntime.MessageCollector.AddMessage(MessageClass.WarningMsg, $"{Title}: {warning}");
        CoreRuntime.MessageCollector.AddMessage(MessageClass.InformationMsg, UiText.ConnectingTo(Title, prepared.Plan.ToString()));
        try
        {
            await _host.StartAsync(prepared.Plan, prepared.Environment);
        }
        catch (Exception ex)
        {
            prepared.Dispose();
            Fail(ex.Message);
            return;
        }

        _prepared = prepared;
        State = SessionState.Running;
        StatusText = prepared.Plan.ToString();
        Connection.OpenConnections.Add(this);
    }

    private void OnExited(object? sender, int? exitCode)
    {
        if (!IsLive)
            return;

        // A killed program may still exit with 0, so a requested disconnect never counts as a clean end.
        bool disconnected = _disconnecting;
        _disconnecting = false;
        Connection.OpenConnections.Remove(this);
        _prepared?.Dispose();
        _prepared = null;
        State = SessionState.Ended;
        StatusText = disconnected ? UiText.Disconnected : UiText.SessionEnded(exitCode);
        CoreRuntime.MessageCollector.AddMessage(MessageClass.InformationMsg, $"{Title}: {StatusText}");
        // An external tool's tab stays, so its output can be read (Reconnect runs it again).
        if (exitCode == 0 && !disconnected && _plan is null)
            RequestClose();
    }

    private void Fail(string reason)
    {
        State = SessionState.Failed;
        StatusText = UiText.CouldNotConnect(reason);
        CoreRuntime.MessageCollector.AddMessage(MessageClass.ErrorMsg, $"{Title}: {StatusText}");
    }
}
