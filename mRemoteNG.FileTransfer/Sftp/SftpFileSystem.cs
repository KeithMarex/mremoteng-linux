using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace mRemoteNG.FileTransfer.Sftp;

/// <summary>How to reach an SFTP server, mostly from <see cref="SshHostConfig"/>.</summary>
public sealed record SftpOptions
{
    public required string Host { get; init; }
    public int Port { get; init; } = 22;
    public required string User { get; init; }

    /// <summary>The connection's stored password, if any.</summary>
    public string? Password { get; init; }

    /// <summary>Private keys to try (files that do not exist are skipped).</summary>
    public IReadOnlyList<string> IdentityFiles { get; init; } = [];

    /// <summary>The known_hosts files to trust host keys from.</summary>
    public IReadOnlyList<string> KnownHostsFiles { get; init; } = [];

    /// <summary>Where a host key the user trusts is added; null to never write.</summary>
    public string? KnownHostsFileToAddTo { get; init; }

    public bool HashKnownHosts { get; init; } = true;

    /// <summary>"ask", "yes", "accept-new" or "no", as in ssh_config.</summary>
    public string StrictHostKeyChecking { get; init; } = "ask";

    /// <summary>The name known_hosts uses for the host, when it is not <see cref="Host"/>.</summary>
    public string? HostKeyAlias { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public static SftpOptions From(SshHostConfig config, string? password) => new()
    {
        Host = config.HostName,
        Port = config.Port,
        User = config.User,
        Password = password,
        IdentityFiles = config.IdentityFiles,
        KnownHostsFiles = [.. config.UserKnownHostsFiles, .. config.GlobalKnownHostsFiles],
        KnownHostsFileToAddTo = config.UserKnownHostsFiles.FirstOrDefault(),
        HashKnownHosts = config.HashKnownHosts,
        StrictHostKeyChecking = config.StrictHostKeyChecking,
        HostKeyAlias = config.HostKeyAlias
    };
}

/// <summary>An SFTP server's files, through SSH.NET.</summary>
public sealed class SftpFileSystem : IRemoteFileSystem
{
    private const int MaxPasswordAttempts = 3;

    private readonly SftpClient _client;

    private SftpFileSystem(SftpClient client, SftpOptions options)
    {
        _client = client;
        Description = options.Port == 22 ? $"sftp://{options.User}@{options.Host}" : $"sftp://{options.User}@{options.Host}:{options.Port}";
    }

    public string Description { get; }

    public bool IsEncrypted => true;

    /// <summary>
    /// Connects and logs in: with the user's keys and the stored password first, then (when the server refuses)
    /// after asking for key passphrases, then for the password, as ssh does.
    /// </summary>
    /// <exception cref="ServerNotTrustedException">The host key was not trusted.</exception>
    /// <exception cref="OperationCanceledException">The user cancelled a question.</exception>
    public static async Task<SftpFileSystem> ConnectAsync(SftpOptions options, IFileTransferPrompts prompts, ConnectionMemory memory,
                                                          CancellationToken cancellationToken)
    {
        List<IPrivateKeySource> keys = [];
        List<string> encryptedKeys = [];
        foreach (string file in options.IdentityFiles.Where(File.Exists))
        {
            if (TryLoadKey(file, memory.Passphrases.GetValueOrDefault(file), out PrivateKeyFile? key, out bool needsPassphrase))
                keys.Add(key!);
            else if (needsPassphrase)
                encryptedKeys.Add(file);
        }

        string? password = memory.Password ?? options.Password;
        bool passwordTyped = memory.Password is not null;
        int passwordAttempts = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HostKeyVerifier verifier = new(options, prompts, memory);
            SftpClient client = new(ConnectionInfoFor(options, keys, password, prompts, verifier.KnownHosts));
            client.HostKeyReceived += (_, e) => e.CanTrust = verifier.Verify(e.HostKey);
            try
            {
                await client.ConnectAsync(cancellationToken);
                if (passwordTyped)
                    memory.Password = password;
                return new SftpFileSystem(client, options);
            }
            catch (SshAuthenticationException) when (encryptedKeys.Count > 0 || passwordAttempts < MaxPasswordAttempts)
            {
                client.Dispose();
                if (encryptedKeys.Count > 0)
                {
                    keys.AddRange(AskPassphrases(encryptedKeys, prompts, memory));
                    encryptedKeys.Clear();
                    continue;
                }

                password = prompts.AskPassword($"{options.User}@{options.Host}'s password:") ?? throw new OperationCanceledException();
                passwordTyped = true;
                passwordAttempts++;
            }
            catch (Exception) when (verifier.Failure is { } failure)
            {
                client.Dispose();
                if (verifier.Cancelled)
                    throw new OperationCanceledException(failure);
                throw new ServerNotTrustedException(failure);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
    }

    public Task<string> GetHomeDirectoryAsync(CancellationToken cancellationToken) => Task.FromResult(_client.WorkingDirectory);

    public async Task<IReadOnlyList<RemoteEntry>> ListAsync(string directory, CancellationToken cancellationToken)
    {
        List<RemoteEntry> entries = [];
        await foreach (ISftpFile file in _client.ListDirectoryAsync(directory, cancellationToken))
        {
            if (file.Name is "." or "..")
                continue;
            RemoteEntryKind kind = file.IsDirectory ? RemoteEntryKind.Directory
                : file.IsRegularFile ? RemoteEntryKind.File
                : file.IsSymbolicLink ? await LinkTargetKindAsync(file.FullName, cancellationToken)
                : RemoteEntryKind.Other;
            entries.Add(new RemoteEntry(file.Name, file.FullName, kind, file.Length, file.LastWriteTime, Permissions(file)));
        }

        return entries;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken) => _client.ExistsAsync(path, cancellationToken);

    public Task DownloadAsync(string path, Stream destination, IProgress<long> bytesTransferred, CancellationToken cancellationToken) =>
        _client.DownloadFileAsync(path, destination,
                                  new ImmediateProgress<DownloadFileProgressReport>(report => bytesTransferred.Report((long)report.TotalBytesDownloaded)),
                                  cancellationToken);

    public Task UploadAsync(Stream source, string path, IProgress<long> bytesTransferred, CancellationToken cancellationToken) =>
        _client.UploadFileAsync(source, path, true,
                                new ImmediateProgress<UploadFileProgressReport>(report => bytesTransferred.Report((long)report.TotalBytesUploaded)),
                                cancellationToken);

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken) => _client.CreateDirectoryAsync(path, cancellationToken);

    public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken) =>
        _client.RenameFileAsync(path, newPath, cancellationToken);

    public async Task DeleteAsync(RemoteEntry entry, CancellationToken cancellationToken)
    {
        if (!entry.IsDirectory)
        {
            await _client.DeleteFileAsync(entry.FullPath, cancellationToken);
            return;
        }

        foreach (RemoteEntry child in await ListAsync(entry.FullPath, cancellationToken))
            await DeleteAsync(child, cancellationToken);
        await _client.DeleteDirectoryAsync(entry.FullPath, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<RemoteEntryKind> LinkTargetKindAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            SftpFileAttributes target = await _client.GetAttributesAsync(path, cancellationToken);
            return target.IsDirectory ? RemoteEntryKind.Directory : target.IsRegularFile ? RemoteEntryKind.File : RemoteEntryKind.Other;
        }
        catch (SshException)
        {
            return RemoteEntryKind.Other; // a dangling link
        }
    }

    private static string Permissions(ISftpFile file)
    {
        char type = file.IsDirectory ? 'd' : file.IsSymbolicLink ? 'l' : '-';
        return string.Concat(type,
                             file.OwnerCanRead ? 'r' : '-', file.OwnerCanWrite ? 'w' : '-', file.OwnerCanExecute ? 'x' : '-',
                             file.GroupCanRead ? 'r' : '-', file.GroupCanWrite ? 'w' : '-', file.GroupCanExecute ? 'x' : '-',
                             file.OthersCanRead ? 'r' : '-', file.OthersCanWrite ? 'w' : '-', file.OthersCanExecute ? 'x' : '-');
    }

    private static Renci.SshNet.ConnectionInfo ConnectionInfoFor(SftpOptions options, List<IPrivateKeySource> keys, string? password,
                                                                IFileTransferPrompts prompts, KnownHosts knownHosts)
    {
        List<AuthenticationMethod> methods = [];
        if (keys.Count > 0)
            methods.Add(new PrivateKeyAuthenticationMethod(options.User, [.. keys]));
        if (password is not null)
            methods.Add(new PasswordAuthenticationMethod(options.User, password));

        // Keyboard-interactive (PAM): the password where one is asked for, anything else (a one-time code) from the user.
        KeyboardInteractiveAuthenticationMethod interactive = new(options.User);
        interactive.AuthenticationPrompt += (_, e) =>
        {
            foreach (AuthenticationPrompt prompt in e.Prompts)
            {
                prompt.Response = password is not null && !prompt.IsEchoed && prompt.Request.Contains("password", StringComparison.OrdinalIgnoreCase)
                    ? password
                    : prompts.AskPassword(prompt.Request.Trim()) ?? string.Empty;
            }
        };
        methods.Add(interactive);

        Renci.SshNet.ConnectionInfo info = new(options.Host, options.Port, options.User, [.. methods]) { Timeout = options.Timeout };

        // Like ssh, prefer the key types known_hosts already has for the host, so a known host is not asked about again.
        IReadOnlyList<string> knownTypes = knownHosts.KeyTypesFor(options.HostKeyAlias ?? options.Host, options.Port);
        int position = 0;
        foreach (string algorithm in info.HostKeyAlgorithms.Keys.ToList())
        {
            if (knownTypes.Contains(KeyTypeOf(algorithm)))
                info.HostKeyAlgorithms.SetPosition(algorithm, position++);
        }

        return info;
    }

    // rsa-sha2-256/512 sign with an "ssh-rsa" key; other algorithms are named after their key type.
    private static string KeyTypeOf(string algorithm) =>
        algorithm.StartsWith("rsa-sha2-", StringComparison.Ordinal) ? "ssh-rsa" : algorithm;

    private static bool TryLoadKey(string file, string? passphrase, out PrivateKeyFile? key, out bool needsPassphrase)
    {
        key = null;
        needsPassphrase = false;
        try
        {
            key = passphrase is null ? new PrivateKeyFile(file) : new PrivateKeyFile(file, passphrase);
            return true;
        }
        catch (SshPassPhraseNullOrEmptyException)
        {
            needsPassphrase = true;
            return false;
        }
        catch (Exception ex) when (ex is SshException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
                                       or NotSupportedException or FormatException or System.Security.Cryptography.CryptographicException)
        {
            // A wrong passphrase, or a key SSH.NET cannot use (such as a security key): ssh may still use it.
            needsPassphrase = passphrase is not null;
            return false;
        }
    }

    private static IEnumerable<PrivateKeyFile> AskPassphrases(IEnumerable<string> files, IFileTransferPrompts prompts, ConnectionMemory memory)
    {
        foreach (string file in files)
        {
            for (int attempt = 0; attempt < MaxPasswordAttempts; attempt++)
            {
                if (prompts.AskPassphrase(file) is not { } passphrase)
                    break;
                if (TryLoadKey(file, passphrase, out PrivateKeyFile? key, out _))
                {
                    memory.Passphrases[file] = passphrase;
                    yield return key!;
                    break;
                }
            }
        }
    }

    /// <summary>Checks the server's host key against known_hosts, following StrictHostKeyChecking like ssh.</summary>
    private sealed class HostKeyVerifier(SftpOptions options, IFileTransferPrompts prompts, ConnectionMemory memory)
    {
        public KnownHosts KnownHosts { get; } = KnownHosts.Load(options.KnownHostsFiles);

        public string? Failure { get; private set; }

        public bool Cancelled { get; private set; }

        public bool Verify(byte[] key)
        {
            string keyText = Convert.ToBase64String(key);
            if (memory.TrustedOnce.ContainsKey(keyText))
                return true;

            string host = options.HostKeyAlias ?? options.Host;
            HostKeyCheck check = KnownHosts.Check(host, options.Port, key);
            switch (check.Status)
            {
                case HostKeyStatus.Trusted:
                    return true;
                case HostKeyStatus.Revoked:
                    Failure = $"The host key of {host} is revoked ({KnownHosts.Fingerprint(key)}).";
                    return false;
                case HostKeyStatus.Changed:
                    Failure = $"The host key of {host} has changed since it was saved in {check.Conflict}. Someone could be " +
                              $"intercepting the connection. If the server got a new key, remove the old one with: ssh-keygen -R " +
                              $"\"{KnownHosts.HostName(host, options.Port)}\"";
                    return false;
            }

            TrustDecision decision = options.StrictHostKeyChecking switch
            {
                "yes" => TrustDecision.Reject,
                "accept-new" or "no" => TrustDecision.TrustAndRemember,
                _ => prompts.ConfirmHostKey(new HostKeyQuestion(host, options.Port, KnownHosts.KeyType(key), KnownHosts.Fingerprint(key),
                                                                check.KnownKeyTypes))
            };
            switch (decision)
            {
                case TrustDecision.Reject:
                    Cancelled = options.StrictHostKeyChecking != "yes";
                    Failure = Cancelled
                        ? $"The host key of {host} was not trusted."
                        : $"The host key of {host} is not in known_hosts, and StrictHostKeyChecking is yes.";
                    return false;
                case TrustDecision.TrustAndRemember when options.KnownHostsFileToAddTo is { } file:
                    KnownHosts.Add(file, host, options.Port, key, options.HashKnownHosts);
                    break;
            }

            memory.TrustedOnce[keyText] = true;
            return true;
        }
    }
}
