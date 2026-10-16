using System.Diagnostics;
using System.Globalization;

namespace mRemoteNG.FileTransfer.Sftp;

/// <summary>What ~/.ssh/config (and /etc/ssh/ssh_config) say for a host, as resolved by "ssh -G".</summary>
public sealed record SshHostConfig
{
    public required string HostName { get; init; }
    public required int Port { get; init; }
    public required string User { get; init; }
    public IReadOnlyList<string> IdentityFiles { get; init; } = [];
    public IReadOnlyList<string> UserKnownHostsFiles { get; init; } = [];
    public IReadOnlyList<string> GlobalKnownHostsFiles { get; init; } = [];

    /// <summary>Whether new known_hosts lines are hashed (Debian and Ubuntu default to yes).</summary>
    public bool HashKnownHosts { get; init; }

    /// <summary>"ask", "yes", "accept-new" or "no".</summary>
    public string StrictHostKeyChecking { get; init; } = "ask";

    /// <summary>The name known_hosts uses for the host (HostKeyAlias), when it is not the host name.</summary>
    public string? HostKeyAlias { get; init; }

    /// <summary>A ProxyJump or ProxyCommand the SFTP connection cannot follow.</summary>
    public string? Proxy { get; init; }
}

/// <summary>Resolves a host's SSH settings with "ssh -G", which prints the effective configuration without connecting.</summary>
public static class OpenSshConfig
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <param name="ssh">The ssh program.</param>
    /// <param name="arguments">ssh's arguments for the connection (options, then "--" and the destination).</param>
    /// <returns>The settings, or null when ssh is not there or refuses.</returns>
    public static async Task<SshHostConfig?> ResolveAsync(string ssh, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new(ssh, ["-G", .. arguments])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        try
        {
            using Process process = Process.Start(start)!;
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            _ = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0
                ? Parse(await output, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
                : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static SshHostConfig? Parse(string output, string home)
    {
        Dictionary<string, List<string>> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in output.Split('\n'))
        {
            string trimmed = line.Trim();
            int space = trimmed.IndexOf(' ');
            if (space <= 0)
                continue;
            string key = trimmed[..space];
            if (!values.TryGetValue(key, out List<string>? list))
                values[key] = list = [];
            list.Add(trimmed[(space + 1)..].Trim());
        }

        string? First(string key) => values.TryGetValue(key, out List<string>? list) ? list[0] : null;
        IReadOnlyList<string> Files(string key, bool spaceSeparated) =>
            values.TryGetValue(key, out List<string>? list)
                ? list.SelectMany(value => spaceSeparated ? value.Split(' ', StringSplitOptions.RemoveEmptyEntries) : [value])
                      .Where(path => path != "none")
                      .Select(path => ExpandHome(path, home))
                      .ToList()
                : [];

        if (First("hostname") is not { } hostName || !int.TryParse(First("port"), NumberStyles.None, CultureInfo.InvariantCulture, out int port))
            return null;

        string? proxyJump = First("proxyjump");
        string? proxyCommand = First("proxycommand");
        return new SshHostConfig
        {
            HostName = hostName,
            Port = port,
            User = First("user") ?? Environment.UserName,
            IdentityFiles = Files("identityfile", spaceSeparated: false),
            UserKnownHostsFiles = Files("userknownhostsfile", spaceSeparated: true),
            GlobalKnownHostsFiles = Files("globalknownhostsfile", spaceSeparated: true),
            HashKnownHosts = First("hashknownhosts") == "yes",
            StrictHostKeyChecking = First("stricthostkeychecking") switch
            {
                "true" or "yes" => "yes",
                "false" or "no" or "off" => "no",
                "accept-new" => "accept-new",
                _ => "ask"
            },
            HostKeyAlias = First("hostkeyalias") is { } alias && alias != "none" ? alias : null,
            Proxy = proxyJump is { } jump && jump != "none" ? "ProxyJump " + jump
                : proxyCommand is { } command && command != "none" ? "ProxyCommand " + command
                : null
        };
    }

    private static string ExpandHome(string path, string home) =>
        path == "~" ? home : path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(home, path[2..]) : path;
}
