using System.Globalization;
using System.Text.RegularExpressions;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;

namespace mRemoteNG.Avalonia.Sessions;

/// <summary>
/// Builds the OpenSSH command line for an SSH connection, the counterpart of the PuTTY command line the Windows app
/// builds (PuttyBase). Using OpenSSH means the user's ~/.ssh/config, agent, keys and known_hosts all apply.
/// </summary>
internal static partial class OpenSshCommand
{
    private const string DefaultPuttySession = "Default Settings";

    public static SessionPlan Build(ConnectionInfo connection, string ssh)
    {
        List<string> warnings = [];
        List<string> arguments = [.. PuttyOptions.ToOpenSsh(connection.SSHOptions, warnings)];

        string? alias = HostAlias(connection.PuttySession, warnings);
        string hostname = connection.Hostname.Trim();
        if (hostname.Length > 0 || alias is null)
            hostname = SessionPlanner.Host(connection);

        int port = SessionPlanner.Port(connection);
        if (port != DefaultPorts.Ssh)
            arguments.AddRange(["-p", port.ToString(CultureInfo.InvariantCulture)]);

        string username = connection.Username.Trim();
        if (username.Length > 0)
        {
            if (!CommandLineValue.IsSafe(username))
                throw new SessionException(UiText.UnsafeUsername);
            arguments.AddRange(["-l", username]);
        }

        string destination = alias ?? hostname;
        string? passwordHost = hostname.Length > 0 ? hostname[(hostname.LastIndexOf('@') + 1)..] : null;
        if (TunnelLease.ServerOf(connection) is { } server)
        {
            // Through an SSH tunnel: to its local end, but as the server, so that its Host entries in ~/.ssh/config and
            // its known_hosts entry still apply (with a port other than 22, known_hosts names it "[host]:port").
            string keyAlias = server.Port == DefaultPorts.Ssh ? server.Host : $"[{server.Host}]:{server.Port}";
            arguments.AddRange(["-o", "HostName=" + hostname.Replace("%", "%%"), "-o", "HostKeyAlias=" + keyAlias]);
            destination = alias ?? server.Host;
            passwordHost = keyAlias; // ssh names the alias in its password prompt
        }
        else if (alias is not null && hostname.Length > 0)
        {
            // A PuTTY session name stands for a Host entry in ~/.ssh/config; the connection's host name still decides
            // where to connect ("%" is ssh_config's token character).
            arguments.AddRange(["-o", "HostName=" + hostname.Replace("%", "%%")]);
        }

        arguments.Add("--");
        arguments.Add(destination);

        if (!string.IsNullOrWhiteSpace(connection.OpeningCommand))
            warnings.Add(UiText.OpeningCommandNotSupported);

        return new SessionPlan(ssh, arguments)
        {
            Warnings = warnings,
            Password = string.IsNullOrEmpty(connection.Password) ? null : connection.Password,
            PasswordHost = passwordHost
        };
    }

    /// <summary>
    /// Whether ssh's <paramref name="prompt"/> asks for the password of <paramref name="host"/>. A prompt that names
    /// another host (a ProxyJump host) does not; one that names no host ("Password:") is taken to.
    /// </summary>
    public static bool IsPasswordPromptFor(string prompt, string? host)
    {
        string text = prompt.Trim();
        if (!text.EndsWith("password:", StringComparison.OrdinalIgnoreCase))
            return false;

        // "user@host's password:" (password authentication) or "(user@host) Password:" (keyboard-interactive)
        Match match = PromptHost().Match(text);
        return !match.Success || host is null || match.Groups["host"].Value.Equals(host, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^(?:\([^)@]*@(?<host>[^)]*)\)|[^@\s]*@(?<host>\S+)'s password:$)")]
    private static partial Regex PromptHost();

    private static string? HostAlias(string? puttySession, List<string> warnings)
    {
        string session = puttySession?.Trim() ?? string.Empty;
        if (session.Length == 0 || session == DefaultPuttySession)
            return null;
        if (CommandLineValue.IsSafe(session))
            return session;

        warnings.Add(UiText.PuttySessionIgnored);
        return null;
    }
}

/// <summary>
/// Translates a connection's SSH options, written for PuTTY on Windows, into OpenSSH options. Only options that mean
/// the same to both are passed on; the rest are reported. A shared connection file must not be able to put arbitrary
/// options (such as a proxy command) on the ssh command line.
/// </summary>
internal static partial class PuttyOptions
{
    private static readonly HashSet<string> Flags = ["-A", "-a", "-X", "-x", "-C", "-4", "-6", "-N", "-t", "-T", "-v"];
    private static readonly HashSet<string> WithValue = ["-L", "-R", "-D"];
    // What OpenSSH does anyway.
    private static readonly HashSet<string> Redundant = ["-2", "-ssh"];

    public static IReadOnlyList<string> ToOpenSsh(string? options, List<string> warnings)
    {
        List<string> result = [];
        List<string> tokens = Tokenize(options);
        for (int i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i];
            if (Flags.Contains(token))
                result.Add(token);
            else if (WithValue.Contains(token) && i + 1 < tokens.Count && CommandLineValue.IsSafe(tokens[i + 1]))
                result.AddRange([token, tokens[++i]]);
            else if (token.StartsWith('-') && !Redundant.Contains(token))
                // Only the option is named: the value of an option such as -pw must not reach the notifications.
                warnings.Add(UiText.SshOptionIgnored(token));
        }

        return result;
    }

    // The same splitting as PuttyBase.AddArgumentString: whitespace-separated, optionally quoted.
    private static List<string> Tokenize(string? options) =>
        string.IsNullOrWhiteSpace(options)
            ? []
            : Token().Matches(options)
                     .Select(match => match.Value)
                     .Select(token => token.Length >= 2 && (token[0] == '"' && token[^1] == '"' || token[0] == '\'' && token[^1] == '\'')
                                 ? token[1..^1]
                                 : token)
                     .Where(token => token.Length > 0)
                     .ToList();

    [GeneratedRegex(@"""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'|\S+")]
    private static partial Regex Token();
}
