using CommunityToolkit.Mvvm.Input;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Config.Serializers.ConnectionSerializers;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Messages;

namespace mRemoteNG.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    public SessionsViewModel Sessions { get; }

    private bool CanConnect => SelectedNode is { IsFolder: false };

    // "Open SFTP" on an SSH connection opens its host's files.
    private bool CanOpenFileTransfer => SelectedNode is { IsFolder: false, Connection.Protocol: ProtocolType.SSH2 or ProtocolType.SFTP };

    /// <summary>Opens the selected connection in a new tab: a terminal, or a file transfer for FTP and SFTP.</summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        ConnectionInfo connection = SelectedNode!.Connection;
        if (FileTransferCompatibility.IsFileTransfer(connection.Protocol))
        {
            Sessions.OpenFileTransfer(connection);
            return;
        }

        if (!SessionPlanner.Supports(connection.Protocol))
        {
            string message = UiText.ProtocolNotSupported(connection.Protocol.ToString());
            CoreRuntime.MessageCollector.AddMessage(MessageClass.ErrorMsg, $"{connection.Name}: {message}");
            await _dialogs.ShowErrorAsync(UiText.Connect, message);
            return;
        }

        Sessions.Open(connection);
    }

    [RelayCommand(CanExecute = nameof(CanOpenFileTransfer))]
    private void OpenFileTransfer() => Sessions.OpenFileTransfer(SelectedNode!.Connection);

    private SessionsViewModel CreateSessions(SessionLauncher launcher)
    {
        FileTransferConnector connector = new(launcher.Programs);
        FileTransferPrompts prompts = new(_dialogs, new CertificatePins(_settings, SaveSettings));
        // The protocol may change in the property panel while the connection stays selected.
        PropertyPanel.PropertyEdited += (_, _) => OpenFileTransferCommand.NotifyCanExecuteChanged();
        return new SessionsViewModel(launcher, connection => new FileTransferViewModel(connection, connector.PrepareAsync, _dialogs, prompts, _dispatch));
    }
}
