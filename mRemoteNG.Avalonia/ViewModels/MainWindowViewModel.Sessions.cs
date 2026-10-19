using CommunityToolkit.Mvvm.Input;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Avalonia.Sessions.Rdp;
using mRemoteNG.Config.Serializers.ConnectionSerializers;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Container;
using mRemoteNG.Messages;

namespace mRemoteNG.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    public SessionsViewModel Sessions { get; }

    private readonly ProgramLauncher _programs;

    private bool CanConnect => SelectedNode is { IsFolder: false };

    // "Open SFTP" on an SSH connection opens its host's files.
    private bool CanOpenFileTransfer => SelectedNode is { IsFolder: false, Connection.Protocol: ProtocolType.SSH2 or ProtocolType.SFTP };

    /// <summary>
    /// Opens the selected connection: in a new tab (a terminal, a file transfer for FTP and SFTP, a remote desktop or VNC),
    /// or in the program that shows it in its own window (the web browser, AnyDesk, an external tool). Its PreExtApp
    /// runs first, and its PostExtApp once the tab's session ends.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        ConnectionInfo connection = SelectedNode!.Connection;
        SessionKind kind = ProtocolRouting.KindOf(connection.Protocol);
        if (kind != SessionKind.NotSupported)
            await RunPreConnectionToolAsync(connection);

        SessionTabViewModel? before = Sessions.Selected;
        switch (kind)
        {
            case SessionKind.Terminal:
                Sessions.Open(connection);
                break;
            case SessionKind.FileTransfer:
                Sessions.OpenFileTransfer(connection);
                break;
            case SessionKind.Rdp:
                Sessions.OpenRdp(connection);
                break;
            case SessionKind.Vnc:
                Sessions.OpenVnc(connection);
                break;
            case SessionKind.Launch:
                await LaunchAsync(connection);
                break;
            case SessionKind.ExternalTool:
                await OpenExternalToolConnectionAsync(connection);
                break;
            default:
                await ReportConnectErrorAsync(connection, ProtocolRouting.NotSupportedReason(connection.Protocol));
                break;
        }

        if (Sessions.Selected is { } opened && opened != before && opened.Connection == connection)
            RunPostConnectionToolWhenEnded(connection, opened);
    }

    private async Task LaunchAsync(ConnectionInfo connection)
    {
        SessionPlan plan;
        Task<int> exited;
        try
        {
            ConnectionInfo target = connection;
            if (SshTunnels.Uses(connection))
            {
                // The web browser is not this app's to watch: the tunnel stays open until the app closes.
                TunnelLease tunnel = await Tunnels.OpenAsync(connection, CancellationToken.None);
                Tunnels.KeepUntilExit(tunnel);
                target = tunnel.Target;
            }

            (plan, exited) = _programs.Launch(target);
        }
        catch (SessionException ex)
        {
            await ReportConnectErrorAsync(connection, ex.Message);
            return;
        }

        CoreRuntime.MessageCollector.AddMessage(MessageClass.InformationMsg, UiText.Opening(connection.Name, plan.ToString()));
        int exitCode = await exited;
        if (exitCode != 0)
        {
            CoreRuntime.MessageCollector.AddMessage(MessageClass.WarningMsg,
                $"{connection.Name}: {UiText.ProgramEnded(Path.GetFileName(plan.Program), exitCode)}");
        }
    }

    private async Task ReportConnectErrorAsync(ConnectionInfo connection, string message)
    {
        CoreRuntime.MessageCollector.AddMessage(MessageClass.ErrorMsg, $"{connection.Name}: {message}");
        await _dialogs.ShowErrorAsync(UiText.Connect, message);
    }

    [RelayCommand(CanExecute = nameof(CanOpenFileTransfer))]
    private void OpenFileTransfer() => Sessions.OpenFileTransfer(SelectedNode!.Connection);

    /// <summary>Opens the SSH tunnels connections go through (their SSH Tunnel setting).</summary>
    internal SshTunnels Tunnels { get; }

    // The SSH connections a connection's SSH Tunnel can name; not the selected connection itself.
    private IReadOnlyList<string> SshTunnelNames() =>
        (_model?.GetRecursiveChildList() ?? [])
        .Where(connection => connection is not ContainerInfo && connection.Protocol == ProtocolType.SSH2 &&
                             connection != PropertyPanel.Connection && connection.Name.Length > 0)
        .Select(connection => connection.Name)
        .Distinct()
        .Order(StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    private SessionsViewModel CreateSessions(SessionLauncher launcher)
    {
        FileTransferConnector connector = new(launcher.Programs);
        CertificatePins pins = new(_settings, SaveSettings);
        FileTransferPrompts prompts = new(_dialogs, pins);
        RdpConnector rdp = new(launcher.Programs, _dialogs, pins);
        VncConnector vnc = new(_dialogs);
        // The protocol and the MAC address may change in the property panel while the connection stays selected.
        PropertyPanel.PropertyEdited += (_, _) =>
        {
            OpenFileTransferCommand.NotifyCanExecuteChanged();
            WakeOnLanCommand.NotifyCanExecuteChanged();
        };
        return new SessionsViewModel(launcher,
            connection => new FileTransferViewModel(connection, connector.PrepareAsync, _dialogs, prompts, _dispatch, _settings, SaveSettings),
            connection => new RdpSessionViewModel(connection, rdp.ConnectAsync, _dispatch),
            connection => new VncSessionViewModel(connection, vnc.ConnectAsync, _dispatch),
            Tunnels);
    }
}
