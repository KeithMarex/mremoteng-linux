using System.Globalization;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Resources.Language;

namespace mRemoteNG.Avalonia.Sessions;

/// <summary>
/// Turns a connection into what to run for it, the way the Windows app starts PuTTY for each protocol: OpenSSH for
/// SSH, the telnet client for Telnet, a TCP relay for Raw, and for Terminal a local shell (or ssh to a remote host).
/// </summary>
public sealed class SessionPlanner(SessionPrograms programs)
{
    /// <summary>Whether this app can open connections of <paramref name="protocol"/>.</summary>
    public static bool Supports(ProtocolType protocol) =>
        protocol is ProtocolType.SSH1 or ProtocolType.SSH2 or ProtocolType.Telnet or ProtocolType.RAW or ProtocolType.Terminal;

    /// <exception cref="SessionException">The connection cannot be opened, with the reason.</exception>
    public SessionPlan Plan(ConnectionInfo connection)
    {
        return connection.Protocol switch
        {
            ProtocolType.SSH2 => OpenSshCommand.Build(connection, programs.Locate(programs.Ssh, "openssh-client")),
            ProtocolType.SSH1 => throw new SessionException(UiText.Ssh1NotSupported),
            ProtocolType.Telnet => Telnet(connection),
            ProtocolType.RAW => Raw(connection),
            ProtocolType.Terminal when IsLocal(connection) => new SessionPlan(programs.Locate(programs.Shell, null), []),
            ProtocolType.Terminal => OpenSshCommand.Build(connection, programs.Locate(programs.Ssh, "openssh-client")),
            _ => throw new SessionException(UiText.ProtocolNotSupported(connection.Protocol.ToString()))
        };
    }

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
        string app = programs.App ?? throw new SessionException(UiText.NeedsAppExecutable);
        return new SessionPlan(app, [RawTcpRelay.Argument, host, port]) { Description = UiText.RawConnection(host, port) };
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

    // Like the Windows Terminal protocol: no host name, or localhost, opens a local shell.
    private static bool IsLocal(ConnectionInfo connection)
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

    /// <summary>The user's login shell, for local terminal sessions.</summary>
    public string Shell { get; init; } = Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } shell ? shell : "/bin/sh";

    /// <summary>
    /// This app's executable: ssh runs it as askpass helper and Raw sessions as relay. Null when the app runs under the
    /// shared "dotnet" host, which cannot be started that way.
    /// </summary>
    public string? App { get; init; } = AppExecutable(Environment.ProcessPath);

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
