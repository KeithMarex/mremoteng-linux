using mRemoteNG.Connection.Protocol;

namespace mRemoteNG.Avalonia.Sessions;

/// <summary>How this app opens a connection of each protocol.</summary>
public enum SessionKind
{
    /// <summary>A terminal tab running a program (ssh, telnet, pwsh) or one of this app's relays.</summary>
    Terminal,

    /// <summary>A file transfer tab (FTP, SFTP).</summary>
    FileTransfer,

    /// <summary>A program that opens its own window, such as the web browser; there is no tab.</summary>
    Launch,

    /// <summary>The connection cannot be opened on Linux; <see cref="ProtocolRouting.NotSupportedReason"/> says why.</summary>
    NotSupported
}

/// <summary>Which kind of session opens each protocol: the one place that decides what this app supports.</summary>
public static class ProtocolRouting
{
    public static SessionKind KindOf(ProtocolType protocol) => protocol switch
    {
        // SSH1 opens a terminal tab that explains why it cannot connect.
        ProtocolType.SSH1 or ProtocolType.SSH2 or ProtocolType.Telnet or ProtocolType.Rlogin or ProtocolType.RAW
            or ProtocolType.Terminal or ProtocolType.PowerShell => SessionKind.Terminal,
        ProtocolType.FTP or ProtocolType.SFTP => SessionKind.FileTransfer,
        ProtocolType.HTTP or ProtocolType.HTTPS or ProtocolType.AnyDesk => SessionKind.Launch,
        _ => SessionKind.NotSupported
    };

    /// <summary>Why connections of <paramref name="protocol"/> cannot be opened, for the user.</summary>
    public static string NotSupportedReason(ProtocolType protocol) => protocol switch
    {
        ProtocolType.WSL => UiText.WslWindowsOnly,
        _ => UiText.ProtocolNotSupported(protocol.ToString())
    };
}
