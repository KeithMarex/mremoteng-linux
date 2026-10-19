using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace mRemoteNG.Avalonia.Services;

/// <summary>
/// Wakes a sleeping or switched-off machine (with Wake-on-LAN enabled in its firmware) with a magic packet: 6 bytes
/// 0xFF, then its MAC address 16 times, as a UDP broadcast to port 9. The packet of the Windows app
/// (mRemoteNG/Tools/WakeOnLan.cs), sent to the broadcast address of each network this computer is on as well: on Linux,
/// 255.255.255.255 only leaves through the interface of the default route.
/// </summary>
public static class WakeOnLan
{
    public const int Port = 9;

    /// <summary>
    /// The 6 bytes of a MAC address written with ':', '-', '.' or spaces between the digits, or without; null when it is
    /// not one.
    /// </summary>
    public static byte[]? ParseMacAddress(string? macAddress)
    {
        string digits = new((macAddress ?? string.Empty).Where(c => c is not (':' or '-' or '.' or ' ')).ToArray());
        return digits.Length == 12 && digits.All(Uri.IsHexDigit) ? Convert.FromHexString(digits) : null;
    }

    public static byte[] MagicPacket(byte[] macAddress)
    {
        if (macAddress.Length != 6)
            throw new ArgumentException("A MAC address has 6 bytes.", nameof(macAddress));

        byte[] packet = new byte[102];
        Array.Fill(packet, (byte)0xFF, 0, 6);
        for (int copy = 1; copy <= 16; copy++)
            macAddress.CopyTo(packet, copy * 6);
        return packet;
    }

    /// <summary>Where the packet goes by default: 255.255.255.255 and the broadcast address of each IPv4 network that is up.</summary>
    public static IReadOnlyList<IPEndPoint> BroadcastTargets()
    {
        List<IPAddress> addresses = [IPAddress.Broadcast];
        foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            foreach (UnicastIPAddressInformation unicast in network.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                    BroadcastAddress(unicast.Address, unicast.IPv4Mask) is { } broadcast && !addresses.Contains(broadcast))
                {
                    addresses.Add(broadcast);
                }
            }
        }

        return addresses.Select(address => new IPEndPoint(address, Port)).ToList();
    }

    /// <summary>A network's broadcast address; null for a network without one (a /31 or /32, such as a VPN's).</summary>
    internal static IPAddress? BroadcastAddress(IPAddress address, IPAddress? mask)
    {
        if (mask is null || address.AddressFamily != AddressFamily.InterNetwork || mask.AddressFamily != AddressFamily.InterNetwork)
            return null;

        byte[] host = address.GetAddressBytes();
        byte[] bits = mask.GetAddressBytes();
        if (bits.Sum(part => System.Numerics.BitOperations.PopCount(part)) is 0 or > 30)
            return null;

        byte[] broadcast = new byte[4];
        for (int i = 0; i < 4; i++)
            broadcast[i] = (byte)(host[i] | ~bits[i]);
        return new IPAddress(broadcast);
    }

    /// <summary>Sends the magic packet to each target; it fails only when it could be sent to none.</summary>
    public static async Task SendAsync(byte[] macAddress, IEnumerable<IPEndPoint> targets)
    {
        byte[] packet = MagicPacket(macAddress);
        using UdpClient client = new(AddressFamily.InterNetwork) { EnableBroadcast = true };
        SocketException? failure = null;
        bool sent = false;
        foreach (IPEndPoint target in targets)
        {
            try
            {
                await client.SendAsync(packet, target);
                sent = true;
            }
            catch (SocketException ex)
            {
                failure ??= ex;
            }
        }

        if (!sent)
            throw failure ?? new SocketException((int)SocketError.NetworkUnreachable);
    }
}
