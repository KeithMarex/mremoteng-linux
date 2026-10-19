using mRemoteNG.Avalonia.Services;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.FileTransfer;
using mRemoteNG.FileTransfer.Ftp;
using mRemoteNG.FileTransfer.Sftp;
using mRemoteNG.Security;

namespace mRemoteNG.Avalonia.Sessions;

/// <summary>An FTP or SFTP server ready to connect to.</summary>
/// <param name="Connect">Opens a connection; the tab opens one to browse and the transfer queue one of its own.</param>
public sealed record FileTransferTarget(
    string? StartDirectory,
    IReadOnlyList<string> Warnings,
    Func<IFileTransferPrompts, ConnectionMemory, CancellationToken, Task<IRemoteFileSystem>> Connect);

/// <summary>
/// Turns an FTP or SFTP connection (or an SSH connection, for "Open SFTP") into a file transfer target. SFTP uses
/// the same ssh_config as the terminal ("ssh -G"): host aliases, user, keys, known_hosts and StrictHostKeyChecking.
/// </summary>
public sealed class FileTransferConnector(SessionPrograms programs)
{
    /// <exception cref="SessionException">The connection cannot be opened.</exception>
    public async Task<FileTransferTarget> PrepareAsync(ConnectionInfo connection, CancellationToken cancellationToken)
    {
        string? startDirectory = FileTransferSettings.StartDirectory(connection);
        if (connection.Protocol == ProtocolType.FTP)
            return new FileTransferTarget(startDirectory, [], FtpConnect(connection));

        List<string> warnings = [];
        SshHostConfig config = await ResolveSshConfigAsync(connection, cancellationToken);
        if (config.Proxy is { } proxy)
            warnings.Add(UiText.SftpIgnoresProxy(proxy));

        SftpOptions options = SftpOptions.From(config, string.IsNullOrEmpty(connection.Password) ? null : connection.Password);
        return new FileTransferTarget(startDirectory, warnings,
                                      async (prompts, memory, token) => await SftpFileSystem.ConnectAsync(options, prompts, memory, token));
    }

    private static Func<IFileTransferPrompts, ConnectionMemory, CancellationToken, Task<IRemoteFileSystem>> FtpConnect(ConnectionInfo connection)
    {
        // The user name and password go into FTP commands, which end at a line break: a shared connection file must
        // not be able to add commands of its own.
        string user = connection.Username.Trim();
        if (user.Any(char.IsControl))
            throw new SessionException(UiText.UnsafeUsername);
        // A new connection's password is null until one is set.
        if ((connection.Password ?? string.Empty).Any(c => c is '\r' or '\n'))
            throw new SessionException(UiText.UnsafePassword);

        FtpOptions options = new()
        {
            Host = SessionPlanner.Host(connection),
            Port = SessionPlanner.Port(connection),
            User = user,
            Password = string.IsNullOrEmpty(connection.Password) ? null : connection.Password,
            Encryption = FileTransferSettings.Encryption(connection),
            Passive = FileTransferSettings.Passive(connection)
        };
        return async (prompts, memory, token) => await FtpFileSystem.ConnectAsync(options, prompts, memory, token);
    }

    private async Task<SshHostConfig> ResolveSshConfigAsync(ConnectionInfo connection, CancellationToken cancellationToken)
    {
        SessionPlan plan = OpenSshCommand.Build(connection, programs.Ssh);
        if (programs.Find(programs.Ssh) is { } ssh
            && await OpenSshConfig.ResolveAsync(ssh, plan.Arguments, cancellationToken) is { } resolved)
        {
            return resolved;
        }

        // Without OpenSSH: the connection's own values and OpenSSH's defaults.
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string user = connection.Username.Trim();
        return new SshHostConfig
        {
            HostName = SessionPlanner.Host(connection),
            Port = SessionPlanner.Port(connection),
            User = user.Length > 0 ? user : Environment.UserName,
            IdentityFiles = new[] { "id_ed25519", "id_ecdsa", "id_rsa" }.Select(name => Path.Combine(home, ".ssh", name)).ToList(),
            UserKnownHostsFiles = [Path.Combine(home, ".ssh", "known_hosts")],
            GlobalKnownHostsFiles = ["/etc/ssh/ssh_known_hosts"],
            HashKnownHosts = true
        };
    }
}

/// <summary>The file transfer settings of a connection, kept in its mRemoteNG for Linux plugin properties.</summary>
public static class FileTransferSettings
{
    public const string EncryptionKey = "mRx.FtpEncryption";
    public const string PassiveKey = "mRx.FtpPassive";
    public const string StartDirectoryKey = "mRx.RemotePath";

    public static FtpEncryption Encryption(ConnectionInfo connection) =>
        Enum.TryParse(connection.GetPluginProperty(EncryptionKey), out FtpEncryption encryption) && Enum.IsDefined(encryption)
            ? encryption
            : FtpEncryption.Explicit;

    public static bool Passive(ConnectionInfo connection) => connection.GetPluginProperty(PassiveKey) != bool.FalseString;

    public static string? StartDirectory(ConnectionInfo connection) =>
        connection.GetPluginProperty(StartDirectoryKey) is { Length: > 0 } path ? path : null;
}

/// <summary>FTPS certificates the user chose to trust, kept in the settings.</summary>
public sealed class CertificatePins(AppSettings settings, Action save)
{
    private readonly Lock _lock = new();

    public bool IsTrusted(string host, int port, string fingerprint)
    {
        lock (_lock)
            return settings.TrustedCertificates.TryGetValue(Key(host, port), out string? pinned) && pinned == fingerprint;
    }

    public void Trust(string host, int port, string fingerprint)
    {
        lock (_lock)
            settings.TrustedCertificates[Key(host, port)] = fingerprint;
        save();
    }

    private static string Key(string host, int port) => $"{host.ToLowerInvariant()}:{port}";
}

/// <summary>Asks the engine's questions with the app's dialogs. Called on worker threads, which wait for the answer.</summary>
public sealed class FileTransferPrompts(IDialogService dialogs, CertificatePins pins) : IFileTransferPrompts
{
    public TrustDecision ConfirmHostKey(HostKeyQuestion question) =>
        dialogs.AskTrustAsync(UiText.UnknownHostKeyTitle, UiText.UnknownHostKey(question)).GetAwaiter().GetResult();

    public TrustDecision ConfirmCertificate(CertificateQuestion question)
    {
        if (pins.IsTrusted(question.Host, question.Port, question.Fingerprint))
            return TrustDecision.TrustOnce;

        TrustDecision decision = dialogs.AskTrustAsync(UiText.UntrustedCertificateTitle, UiText.UntrustedCertificate(question)).GetAwaiter().GetResult();
        if (decision == TrustDecision.TrustAndRemember)
            pins.Trust(question.Host, question.Port, question.Fingerprint);
        return decision;
    }

    public string? AskPassword(string prompt) => dialogs.AskSecretAsync(prompt).GetAwaiter().GetResult()?.ConvertToUnsecureString();

    public string? AskPassphrase(string keyFile) =>
        dialogs.AskSecretAsync(UiText.PassphrasePrompt(keyFile)).GetAwaiter().GetResult()?.ConvertToUnsecureString();
}
