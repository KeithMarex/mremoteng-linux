using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Avalonia.Sessions.Rdp;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Messages;

namespace mRemoteNG.Avalonia.ViewModels;

/// <summary>Where an RDP tab shows its session; implemented by its view.</summary>
public interface IRdpView
{
    /// <summary>The window to embed in and the sizes, as they are now.</summary>
    RdpDisplay Describe();

    /// <summary>Gives the session the keyboard.</summary>
    void FocusSession();
}

/// <summary>
/// An RDP session tab: xfreerdp, shown inside the tab (or in its own window for full screen). Like the terminal tabs it
/// is listed in the connection's OpenConnections while it runs; a session that ends normally (logoff) closes its tab,
/// otherwise the tab says why and offers Reconnect.
/// </summary>
public sealed partial class RdpSessionViewModel : SessionTabViewModel, IProtocolSession
{
    private readonly Func<ConnectionInfo, RdpDisplay, CancellationToken, Task<RdpSession>> _connect;
    private readonly Action<Action> _dispatch;
    private IRdpView? _view;
    private RdpSession? _session;
    private CancellationTokenSource? _connecting;
    private bool _disconnecting;

    /// <param name="connect">Asks what is needed and starts xfreerdp (RdpConnector).</param>
    /// <param name="dispatch">Queues an action on the UI thread.</param>
    public RdpSessionViewModel(ConnectionInfo connection, Func<ConnectionInfo, RdpDisplay, CancellationToken, Task<RdpSession>> connect,
                               Action<Action> dispatch) : base(connection)
    {
        _connect = connect;
        _dispatch = dispatch;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive), nameof(HasEnded), nameof(ShowsStatus))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectCommand))]
    private SessionState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive), nameof(HasEnded), nameof(ShowsStatus))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectCommand))]
    private bool _isConnecting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsStatus))]
    private bool _inOwnWindow;

    public override bool IsLive => IsConnecting || State == SessionState.Running;

    public override bool HasEnded => !IsConnecting && State is SessionState.Ended or SessionState.Failed;

    /// <summary>Whether the tab shows a status line instead of (or above) the session.</summary>
    public bool ShowsStatus => IsConnecting || State != SessionState.Running || InOwnWindow;

    /// <summary>Called by the view once it has a window to show the session in; connects the first time.</summary>
    public async Task AttachAsync(IRdpView view)
    {
        _view = view;
        if (State == SessionState.NotStarted && !IsConnecting)
            await ConnectAsync();
    }

    /// <summary>Gives the session the keyboard; the view calls it when the tab is shown.</summary>
    public void Focus()
    {
        if (State == SessionState.Running && !InOwnWindow)
            _view?.FocusSession();
    }

    /// <summary>Ends the session (IProtocolSession); the tab stays open.</summary>
    public void Disconnect()
    {
        _connecting?.Cancel();
        if (_session is null)
            return;
        _disconnecting = true;
        _session.Process.Stop();
    }

    private bool CanReconnect => HasEnded;

    [RelayCommand(CanExecute = nameof(CanReconnect))]
    private Task ReconnectAsync() => ConnectAsync();

    protected override void EndSession() => Disconnect();

    protected override void OnDisposed()
    {
        Connection.OpenConnections.Remove(this);
        _session?.Process.Dispose();
        _session = null;
    }

    private async Task ConnectAsync()
    {
        if (_view is null || IsLive || IsDisposed)
            return;

        IsConnecting = true;
        _disconnecting = false;
        StatusText = UiText.Connecting;
        using CancellationTokenSource connecting = new();
        _connecting = connecting;
        RdpSession session;
        try
        {
            session = await _connect(Connection, _view.Describe(), connecting.Token);
        }
        catch (Exception ex) when (ex is SessionException or OperationCanceledException)
        {
            IsConnecting = false;
            Fail(ex is SessionException ? ex.Message : UiText.ConnectionCancelled);
            return;
        }
        finally
        {
            _connecting = null;
        }

        IsConnecting = false;
        if (IsDisposed)
        {
            session.Process.Dispose();
            return;
        }

        foreach (string warning in session.Plan.Warnings)
            CoreRuntime.MessageCollector.AddMessage(MessageClass.WarningMsg, $"{Title}: {warning}");
        CoreRuntime.MessageCollector.AddMessage(MessageClass.InformationMsg, UiText.ConnectingTo(Title, session.Plan.ToString()));
        _session = session;
        session.Process.Exited += (_, exit) => _dispatch(() => OnExited(session, exit));
        InOwnWindow = session.Plan.OwnWindow;
        State = SessionState.Running;
        StatusText = InOwnWindow ? UiText.RdpInOwnWindow : string.Empty;
        Connection.OpenConnections.Add(this);
    }

    private void OnExited(RdpSession session, RdpExit exit)
    {
        if (session != _session)
            return;

        _session = null;
        session.Process.Dispose();
        Connection.OpenConnections.Remove(this);
        bool disconnected = _disconnecting;
        _disconnecting = false;
        if (disconnected)
        {
            State = SessionState.Ended;
            StatusText = UiText.Disconnected;
            return;
        }

        if (FreeRdpProgram.IsNormalEnd(exit.ExitCode))
        {
            State = SessionState.Ended;
            StatusText = FreeRdpProgram.Reason(exit.ExitCode) ?? UiText.SessionEnded(exit.ExitCode);
            CoreRuntime.MessageCollector.AddMessage(MessageClass.InformationMsg, $"{Title}: {StatusText}");
            RequestClose();
            return;
        }

        string reason = FreeRdpProgram.Reason(exit.ExitCode) ?? exit.LastError ?? UiText.SessionEnded(exit.ExitCode);
        State = SessionState.Failed;
        StatusText = reason;
        string details = exit.LastError is { } error && error != reason ? $" ({error})" : string.Empty;
        CoreRuntime.MessageCollector.AddMessage(MessageClass.ErrorMsg, $"{Title}: {reason}{details}");
    }

    private void Fail(string reason)
    {
        State = SessionState.Failed;
        StatusText = UiText.CouldNotConnect(reason);
        CoreRuntime.MessageCollector.AddMessage(MessageClass.ErrorMsg, $"{Title}: {StatusText}");
    }
}
