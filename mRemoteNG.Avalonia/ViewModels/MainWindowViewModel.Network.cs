using System.Net;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.Input;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Services;
using mRemoteNG.Connection;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;

namespace mRemoteNG.Avalonia.ViewModels;

// Wake-on-LAN.
public partial class MainWindowViewModel
{
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
}
