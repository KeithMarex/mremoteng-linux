using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Connection.Protocol.VNC;
using mRemoteNG.Messages;
using mRemoteNG.Vnc;

namespace mRemoteNG.Avalonia.ViewModels;

/// <summary>
/// A VNC or Apple Remote Desktop session tab, drawn by the app's own viewer. Like the other tabs it is listed in the
/// connection's OpenConnections while connected; when the session drops the tab stays, with the reason and Reconnect.
/// </summary>
public sealed partial class VncSessionViewModel : SessionTabViewModel, IProtocolSession
{
    // X11 keysyms of Control_L, Alt_L and Delete.
    private static readonly uint[] CtrlAltDel = [0xFFE3, 0xFFE9, 0xFFFF];

    private readonly Func<ConnectionInfo, CancellationToken, Task<RfbClient>> _connect;
    private readonly Action<Action> _dispatch;
    private CancellationTokenSource? _connecting;
    private bool _disconnecting;

    /// <param name="connect">Connects and logs in (VncConnector).</param>
    /// <param name="dispatch">Queues an action on the UI thread.</param>
    public VncSessionViewModel(ConnectionInfo connection, Func<ConnectionInfo, CancellationToken, Task<RfbClient>> connect,
                               Action<Action> dispatch) : base(connection)
    {
        _connect = connect;
        _dispatch = dispatch;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive), nameof(HasEnded))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectCommand), nameof(SendCtrlAltDelCommand), nameof(RefreshCommand))]
    private SessionState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive), nameof(HasEnded))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectCommand))]
    private bool _isConnecting;

    /// <summary>The session the view shows; null while not connected.</summary>
    [ObservableProperty]
    private RfbClient? _client;

    public override bool IsLive => IsConnecting || State == SessionState.Running;

    public override bool HasEnded => !IsConnecting && State is SessionState.Ended or SessionState.Failed;

    /// <summary>Whether keys and the mouse are kept from the remote computer.</summary>
    public bool ViewOnly => Connection.VNCViewOnly;

    /// <summary>How the remote screen is fitted into the tab.</summary>
    public SmartSizeMode SmartSize => Connection.VNCSmartSizeMode;

    /// <summary>Starts the session the first time the tab is shown.</summary>
    public async Task AttachAsync()
    {
        if (State == SessionState.NotStarted && !IsConnecting)
            await ConnectAsync();
    }

    /// <summary>Ends the session (IProtocolSession); the tab stays open.</summary>
    public void Disconnect()
    {
        _connecting?.Cancel();
        if (Client is null)
            return;
        _disconnecting = true;
        Client.Dispose();
    }

    private bool CanReconnect => HasEnded;

    [RelayCommand(CanExecute = nameof(CanReconnect))]
    private Task ReconnectAsync() => ConnectAsync();

    private bool IsRunning => State == SessionState.Running && !ViewOnly;

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void SendCtrlAltDel()
    {
        foreach (uint key in CtrlAltDel)
            Client?.SendKey(key, down: true);
        foreach (uint key in CtrlAltDel.Reverse())
            Client?.SendKey(key, down: false);
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Refresh() => Client?.Refresh();

    protected override void EndSession() => Disconnect();

    protected override void OnDisposed()
    {
        Connection.OpenConnections.Remove(this);
        Client?.Dispose();
    }

    private async Task ConnectAsync()
    {
        if (IsLive || IsDisposed)
            return;

        IsConnecting = true;
        _disconnecting = false;
        StatusText = UiText.Connecting;
        using CancellationTokenSource connecting = new();
        _connecting = connecting;
        RfbClient client;
        try
        {
            client = await _connect(await OpenTunnelAsync(connecting.Token), connecting.Token);
        }
        catch (Exception ex) when (ex is SessionException or OperationCanceledException)
        {
            IsConnecting = false;
            CloseTunnel();
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
            client.Dispose();
            return;
        }

        client.Closed += error => _dispatch(() => OnClosed(client, error));
        // Setting Client lets the view subscribe to the screen's updates before they start.
        Client = client;
        client.Start();
        State = SessionState.Running;
        StatusText = UiText.ConnectedTo(client.DesktopName.Length > 0 ? client.DesktopName : Title);
        Connection.OpenConnections.Add(this);
        CoreRuntime.MessageCollector.AddMessage(MessageClass.InformationMsg, $"{Title}: {StatusText}");
    }

    private void OnClosed(RfbClient client, Exception? error)
    {
        if (client != Client)
            return;

        Client = null;
        CloseTunnel();
        Connection.OpenConnections.Remove(this);
        bool disconnected = _disconnecting;
        _disconnecting = false;
        if (disconnected || error is null)
        {
            State = SessionState.Ended;
            StatusText = UiText.Disconnected;
            return;
        }

        State = SessionState.Failed;
        StatusText = UiText.ConnectionLost(error.Message);
        CoreRuntime.MessageCollector.AddMessage(MessageClass.ErrorMsg, $"{Title}: {StatusText}");
    }

    private void Fail(string reason)
    {
        State = SessionState.Failed;
        StatusText = UiText.CouldNotConnect(reason);
        CoreRuntime.MessageCollector.AddMessage(MessageClass.ErrorMsg, $"{Title}: {StatusText}");
    }
}
