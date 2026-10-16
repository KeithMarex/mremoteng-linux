using CommunityToolkit.Mvvm.Input;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Connection;
using mRemoteNG.Messages;

namespace mRemoteNG.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel
{
    public SessionsViewModel Sessions { get; }

    private bool CanConnect => SelectedNode is { IsFolder: false };

    /// <summary>Opens the selected connection in a new session tab.</summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        ConnectionInfo connection = SelectedNode!.Connection;
        if (!SessionPlanner.Supports(connection.Protocol))
        {
            string message = UiText.ProtocolNotSupported(connection.Protocol.ToString());
            CoreRuntime.MessageCollector.AddMessage(MessageClass.ErrorMsg, $"{connection.Name}: {message}");
            await _dialogs.ShowErrorAsync(UiText.Connect, message);
            return;
        }

        Sessions.Open(connection);
    }
}
