using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.Input;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Services;
using mRemoteNG.Connection;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;

namespace mRemoteNG.Avalonia.ViewModels;

// Wake-on-LAN and the host status.
public partial class MainWindowViewModel
{
    /// <summary>Checks which servers answer while View > Host Status is on.</summary>
    internal HostStatusMonitor HostStatusChecks { get; } = new();

    /// <summary>Where Wake-on-LAN packets go: the broadcast addresses, or a local port in tests.</summary>
    internal Func<IEnumerable<IPEndPoint>> WakeOnLanTargets { get; set; } = WakeOnLan.BroadcastTargets;

    // A connection with a MAC address, or a folder holding one.
    private bool CanWakeOnLan => SelectedNode is { IsRoot: false } node && ConnectionsIn(node.Connection).Any(HasMacAddress);

    /// <summary>Wakes the selected connection, or every connection with a MAC address in the selected folder.</summary>
    [RelayCommand(CanExecute = nameof(CanWakeOnLan))]
    private async Task WakeOnLanAsync()
    {
        List<string> invalid = [];
        foreach (ConnectionInfo connection in ConnectionsIn(SelectedNode!.Connection).Where(HasMacAddress))
        {
            string macAddress = connection.MacAddress.Trim();
            if (WakeOnLan.ParseMacAddress(macAddress) is not { } bytes)
            {
                invalid.Add($"{connection.Name}: {macAddress}");
                continue;
            }

            try
            {
                await WakeOnLan.SendAsync(bytes, WakeOnLanTargets());
                CoreRuntime.MessageCollector.AddMessage(MessageClass.InformationMsg,
                                                        $"{connection.Name}: {string.Format(Language.WakeOnLanSent, macAddress)}");
            }
            catch (SocketException ex)
            {
                CoreRuntime.MessageCollector.AddMessage(MessageClass.ErrorMsg,
                                                        $"{connection.Name}: {string.Format(Language.WakeOnLanFailed, macAddress)} {ex.Message}");
            }
        }

        if (invalid.Count > 0)
            await _dialogs.ShowErrorAsync(UiText.WakeOnLan, UiText.InvalidMacAddresses(invalid));
    }

    private static bool HasMacAddress(ConnectionInfo connection) => !string.IsNullOrWhiteSpace(connection.MacAddress);

    // Checks the tree's servers while the user wants to see it and a file is open; again from the start for a new file.
    private void ShowHostStatus()
    {
        if (_settings.ShowHostStatus && _model is not null)
            HostStatusChecks.Start(AllNodes);
        else
            HostStatusChecks.Stop();
    }

    // A connection that now points somewhere else is checked at once, not at the next round.
    private void CheckHostAgain(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConnectionInfo.Hostname) or nameof(ConnectionInfo.Port) or nameof(ConnectionInfo.Protocol)
                or nameof(ConnectionInfo.SSHTunnelConnectionName) &&
            sender is ConnectionInfo connection && FindNode(connection) is { } node)
        {
            _ = HostStatusChecks.CheckAsync(node);
        }
    }
}
