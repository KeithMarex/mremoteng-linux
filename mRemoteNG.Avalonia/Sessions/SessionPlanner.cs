using System.Globalization;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Resources.Language;

namespace mRemoteNG.Avalonia.Sessions;

/// <summary>
/// Turns a connection into what its terminal runs, the way the Windows app starts PuTTY for each protocol: OpenSSH for
/// SSH, the telnet and rlogin clients for Telnet and Rlogin, this app as relay for Raw, pwsh for PowerShell, and for
/// Terminal a local shell (or ssh to a remote host).
/// </summary>
public sealed class SessionPlanner(SessionPrograms programs)
{
    /// <exception cref="SessionException">The connection cannot be opened, with the reason.</exception>
    public SessionPlan Plan(ConnectionInfo connection)
    {
        return connection.Protocol switch
        {
            ProtocolType.SSH2 => OpenSshCommand.Build(connection, programs.Locate(programs.Ssh, "openssh-client")),
            ProtocolType.SSH1 => throw new SessionException(UiText.Ssh1NotSupported),
            ProtocolType.Telnet => Telnet(connection),
            ProtocolType.RAW => Raw(connection),
            ProtocolType.Rlogin => Rlogin(connection),
            ProtocolType.PowerShell when IsLocal(connection) => new SessionPlan(PowerShell(), ["-NoLogo"]),
            ProtocolType.PowerShell => PowerShellCommand.Build(connection, PowerShell()),
            ProtocolType.Terminal when IsLocal(connection) => new SessionPlan(programs.Locate(programs.Shell, null), []),
            ProtocolType.Terminal => OpenSshCommand.Build(connection, programs.Locate(programs.Ssh, "openssh-client")),
            _ => throw new SessionException(ProtocolRouting.NotSupportedReason(connection.Protocol))
        };
    }

    private string PowerShell() => programs.Locate(programs.PowerShell, "powershell");

    private SessionPlan Telnet(ConnectionInfo connection)
    {
        string host = Host(connection);
        string port = Port(connection).ToString(CultureInfo.InvariantCulture);
        // As on Windows, a telnet session does not log in with the connection's credentials.
        return new SessionPlan(programs.Locate(programs.Telnet, "inetutils-telnet"), ["--", host, port]);
    }

    // This app relays the bytes; the pseudo-terminal echoes and edits lines locally, as PuTTY does for Raw.
    private SessionPlan Raw(ConnectionInfo connection)
    {
        string host = Host(connection);
        string port = Port(connection).ToString(CultureInfo.InvariantCulture);
        string app = programs.App ?? throw new SessionException(UiText.NeedsAppExecutable("Raw"));
        return new SessionPlan(app, [RawTcpRelay.Argument, host, port]) { Description = UiText.RawConnection(host, port) };
    }

    // rlogin servers only accept clients on a privileged port, so the rlogin client is setuid root (rsh-redone-client
    // on Debian 12 and Ubuntu). Without it this app speaks rlogin itself, as PuTTY does on Windows; that reaches only
    // servers that accept any port.
    private SessionPlan Rlogin(ConnectionInfo connection)
    {
        string host = Host(connection);
        int port = Port(connection);
        string portText = port.ToString(CultureInfo.InvariantCulture);
        if (programs.Find(programs.Rlogin) is { } rlogin)
            return new SessionPlan(rlogin, port == DefaultPorts.Rlogin ? ["--", host] : ["-p", portText, "--", host]);

        string app = programs.App ?? throw new SessionException(UiText.NeedsAppExecutable("Rlogin"));
        return new SessionPlan(app, [RloginRelay.Argument, host, portText])
        {
            Description = UiText.RloginConnection(host, portText),
            Warnings = [UiText.NoRloginClient]
        };
    }

    /// <summary>The connection's host name, checked so that it cannot be taken for a command-line option.</summary>
    internal static string Host(ConnectionInfo connection)
    {
        string host = connection.Hostname.Trim();
        if (host.Length == 0)
            throw new SessionException(Language.ConnectionOpenFailedNoHostname);
        // Connection files are shared; never echo a rejected value, it may hold control characters.
        if (!CommandLineValue.IsSafe(host))
            throw new SessionException(UiText.UnsafeHostname);
        return host;
    }

    /// <summary>The connection's port, or the protocol's default when none is set.</summary>
    internal static int Port(ConnectionInfo connection)
    {
        int port = connection.Port > 0 ? connection.Port : DefaultPorts.For(connection.Protocol);
        if (port is < 1 or > 65535)
            throw new SessionException(UiText.InvalidPort(port));
        return port;
    }

    // Like the Windows Terminal and PowerShell protocols: no host name, or localhost, opens a local shell.
    internal static bool IsLocal(ConnectionInfo connection)
    {
        string host = connection.Hostname.Trim();
        return host.Length == 0 || host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>The programs sessions run, and how they are found.</summary>
public sealed class SessionPrograms
{
    public string Ssh { get; init; } = "ssh";

    public string Telnet { get; init; } = "telnet";

    public string Rlogin { get; init; } = "rlogin";

    /// <summary>FreeRDP 3's X11 client, for RDP.</summary>
    public string FreeRdp { get; init; } = "xfreerdp3";

    public string PowerShell { get; init; } = "pwsh";

    /// <summary>Opens web addresses in the user's browser.</summary>
    public string Browser { get; init; } = "xdg-open";

    public string AnyDesk { get; init; } = "anydesk";

    /// <summary>virt-viewer's client, for SPICE.</summary>
    public string RemoteViewer { get; init; } = "remote-viewer";

    /// <summary>The user's login shell, for local terminal sessions.</summary>
    public string Shell { get; init; } = Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } shell ? shell : "/bin/sh";

    /// <summary>
    /// This app's executable: ssh runs it as askpass helper and Raw sessions as relay. Null when the app runs under the
    /// shared "dotnet" host, which cannot be started that way.
    /// </summary>
    public string? App { get; init; } = AppExecutable(Environment.ProcessPath);

    /// <summary>Starts a program that opens its own window; the task ends with its exit code.</summary>
    public Func<SessionPlan, Task<int>> StartDetached { get; init; } = ProgramLauncher.Start;

    /// <summary>Finds a program on the PATH (or checks a path); null when it is not installed.</summary>
    public Func<string, string?> Find { get; init; } = FindOnPath;

    /// <exception cref="SessionException">The program is not installed.</exception>
    internal string Locate(string program, string? package) =>
        Find(program) ?? throw new SessionException(UiText.ProgramNotFound(program, package));

    internal static string? AppExecutable(string? processPath) =>
        string.IsNullOrEmpty(processPath) || Path.GetFileNameWithoutExtension(processPath) == "dotnet" ? null : processPath;

    private static string? FindOnPath(string program)
    {
        if (program.Contains('/'))
            return File.Exists(program) ? program : null;

        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(':', StringSplitOptions.RemoveEmptyEntries)
                   .Select(directory => Path.Combine(directory, program))
                   .FirstOrDefault(IsExecutable);
    }

    private static bool IsExecutable(string file) =>
        File.Exists(file) && (OperatingSystem.IsWindows() || (File.GetUnixFileMode(file) & (UnixFileMode.UserExecute |
            UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0);
}

/// <summary>Checks values from a connection file before they go on a command line.</summary>
internal static class CommandLineValue
{
    /// <summary>
    /// True for a non-empty value without whitespace or control characters that does not start with '-'. Connection
    /// files are shared, so a host name such as "-oProxyCommand=..." must never reach ssh as an option.
    /// </summary>
    public static bool IsSafe(string? value) =>
        !string.IsNullOrEmpty(value) && value[0] != '-' && !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c));
}
