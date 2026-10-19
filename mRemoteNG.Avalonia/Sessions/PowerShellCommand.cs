using System.Globalization;
using mRemoteNG.Connection;

namespace mRemoteNG.Avalonia.Sessions;

/// <summary>
/// Builds the pwsh command line for a remote PowerShell connection. The Windows app uses WinRM
/// (Enter-PSSession -ComputerName); PowerShell on Linux remotes over SSH (Enter-PSSession -HostName), so the server
/// needs PowerShell's SSH subsystem. pwsh runs ssh, which gets the stored password from the askpass helper.
/// </summary>
internal static class PowerShellCommand
{
    // The WinRM ports the Windows app's default (5985) and HTTPS (5986) stand for; SSH remoting uses ssh's port.
    private static readonly int[] WinRmPorts = [5985, 5986];

    private const int SshPort = 22;

    public static SessionPlan Build(ConnectionInfo connection, string pwsh)
    {
        List<string> warnings = [];
        string host = SessionPlanner.Host(connection);
        string command = "Enter-PSSession -HostName " + Quote(host);

        string username = connection.Username.Trim();
        if (username.Length > 0)
        {
            string domain = connection.Domain.Trim();
            if (!IsSafe(username) || (domain.Length > 0 && !IsSafe(domain)))
                throw new SessionException(UiText.UnsafeUsername);
            command += " -UserName " + Quote(domain.Length > 0 ? $"{domain}\\{username}" : username);
        }

        int port = SessionPlanner.Port(connection);
        if (WinRmPorts.Contains(port))
            warnings.Add(UiText.PowerShellUsesSsh(port));
        else if (port != SshPort)
            command += " -Port " + port.ToString(CultureInfo.InvariantCulture);

        // -NoExit: Enter-PSSession only takes effect in an interactive pwsh.
        return new SessionPlan(pwsh, ["-NoLogo", "-NoExit", "-Command", command])
        {
            Warnings = warnings,
            Password = string.IsNullOrEmpty(connection.Password) ? null : connection.Password,
            PasswordHost = host
        };
    }

    // PowerShell also ends a single-quoted string at the typographic single quotes; such values are refused.
    private static bool IsSafe(string value) => CommandLineValue.IsSafe(value) && !value.Any(c => c is '\'' or '‘' or '’' or '‚' or '‛');

    private static string Quote(string value) => IsSafe(value)
        ? $"'{value}'"
        : throw new SessionException(UiText.UnsafeHostname);
}
