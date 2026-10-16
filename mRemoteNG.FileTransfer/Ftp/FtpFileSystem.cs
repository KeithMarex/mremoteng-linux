using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using FluentFTP;
using FluentFTP.Exceptions;

namespace mRemoteNG.FileTransfer.Ftp;

public enum FtpEncryption
{
    /// <summary>FTPS: the connection starts plain and switches to TLS (AUTH TLS) before logging in. TLS is required.</summary>
    Explicit,

    /// <summary>FTPS: TLS from the first byte, usually on port 990.</summary>
    Implicit,

    /// <summary>Plain FTP: the password and the files go over the network unencrypted.</summary>
    None
}

/// <summary>How to reach an FTP server.</summary>
public sealed record FtpOptions
{
    public required string Host { get; init; }
    public int Port { get; init; } = 21;

    /// <summary>The user; empty logs in anonymously.</summary>
    public string User { get; init; } = string.Empty;

    public string? Password { get; init; }
    public FtpEncryption Encryption { get; init; } = FtpEncryption.Explicit;

    /// <summary>Passive mode (the client opens the data connections), which works through NAT and most firewalls.</summary>
    public bool Passive { get; init; } = true;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>An FTP or FTPS server's files, through FluentFTP.</summary>
public sealed class FtpFileSystem : IRemoteFileSystem
{
    private const string Anonymous = "anonymous";
    private const int MaxPasswordAttempts = 3;

    private readonly AsyncFtpClient _client;

    private FtpFileSystem(AsyncFtpClient client, FtpOptions options, string user)
    {
        _client = client;
        IsEncrypted = options.Encryption != FtpEncryption.None;
        string scheme = IsEncrypted ? "ftps" : "ftp";
        Description = $"{scheme}://{user}@{options.Host}{(options.Port == 21 ? string.Empty : ":" + options.Port)}";
    }

    public string Description { get; }

    public bool IsEncrypted { get; }

    /// <exception cref="ServerNotTrustedException">The server's certificate was not trusted.</exception>
    /// <exception cref="OperationCanceledException">The user cancelled a question.</exception>
    public static async Task<FtpFileSystem> ConnectAsync(FtpOptions options, IFileTransferPrompts prompts, ConnectionMemory memory,
                                                         CancellationToken cancellationToken)
    {
        string user = string.IsNullOrWhiteSpace(options.User) ? Anonymous : options.User.Trim();
        string? password = memory.Password ?? options.Password;
        bool passwordTyped = memory.Password is not null;
        if (password is null && user != Anonymous)
        {
            password = prompts.AskPassword($"Password for {user} on {options.Host}:") ?? throw new OperationCanceledException();
            passwordTyped = true;
        }

        for (int attempt = 1; ; attempt++)
        {
            string? certificateFailure = null;
            bool certificateCancelled = false;
            FtpConfig config = new()
            {
                EncryptionMode = options.Encryption switch
                {
                    FtpEncryption.Implicit => FtpEncryptionMode.Implicit,
                    FtpEncryption.None => FtpEncryptionMode.None,
                    _ => FtpEncryptionMode.Explicit
                },
                DataConnectionType = options.Passive ? FtpDataConnectionType.AutoPassive : FtpDataConnectionType.AutoActive,
                ConnectTimeout = (int)options.Timeout.TotalMilliseconds,
                ReadTimeout = (int)options.Timeout.TotalMilliseconds,
                ValidateAnyCertificate = false
            };
            AsyncFtpClient client = new(options.Host, user, password ?? "anonymous@", options.Port, config);
            client.ValidateCertificate += (_, e) =>
            {
                e.Accept = TrustCertificate(options, e.Certificate, e.PolicyErrors, e.PolicyErrorMessage, prompts, memory, out certificateFailure,
                                            out certificateCancelled);
            };
            try
            {
                await client.Connect(cancellationToken);
                if (passwordTyped)
                    memory.Password = password;
                return new FtpFileSystem(client, options, user);
            }
            catch (FtpAuthenticationException) when (attempt < MaxPasswordAttempts && user != Anonymous)
            {
                client.Dispose();
                password = prompts.AskPassword($"Password for {user} on {options.Host}:") ?? throw new OperationCanceledException();
                passwordTyped = true;
            }
            catch (Exception) when (certificateFailure is not null)
            {
                client.Dispose();
                if (certificateCancelled)
                    throw new OperationCanceledException(certificateFailure);
                throw new ServerNotTrustedException(certificateFailure);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
    }

    public async Task<string> GetHomeDirectoryAsync(CancellationToken cancellationToken) => await _client.GetWorkingDirectory(cancellationToken);

    public async Task<IReadOnlyList<RemoteEntry>> ListAsync(string directory, CancellationToken cancellationToken)
    {
        FtpListItem[] items = await _client.GetListing(directory, FtpListOption.AllFiles, cancellationToken);
        return items.Where(item => item.Name is not ("." or ".."))
                    .Select(item => new RemoteEntry(item.Name, item.FullName,
                                                    item.Type switch
                                                    {
                                                        FtpObjectType.Directory => RemoteEntryKind.Directory,
                                                        FtpObjectType.File => RemoteEntryKind.File,
                                                        _ => RemoteEntryKind.Other
                                                    },
                                                    item.Size,
                                                    item.Modified == DateTime.MinValue ? null : item.Modified,
                                                    item.RawPermissions ?? string.Empty))
                    .ToList();
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken) =>
        await _client.FileExists(path, cancellationToken) || await _client.DirectoryExists(path, cancellationToken);

    public async Task DownloadAsync(string path, Stream destination, IProgress<long> bytesTransferred, CancellationToken cancellationToken)
    {
        if (!await _client.DownloadStream(destination, path, 0,
                                          new ImmediateProgress<FtpProgress>(progress => bytesTransferred.Report(progress.TransferredBytes)),
                                          cancellationToken))
        {
            throw new IOException($"{path} could not be downloaded.");
        }
    }

    public async Task UploadAsync(Stream source, string path, IProgress<long> bytesTransferred, CancellationToken cancellationToken)
    {
        FtpStatus status = await _client.UploadStream(source, path, FtpRemoteExists.Overwrite, false,
                                                      new ImmediateProgress<FtpProgress>(progress => bytesTransferred.Report(progress.TransferredBytes)),
                                                      cancellationToken);
        if (status == FtpStatus.Failed)
            throw new IOException($"{path} could not be uploaded.");
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        if (!await _client.CreateDirectory(path, cancellationToken))
            throw new IOException($"{path} could not be created.");
    }

    public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken) =>
        _client.Rename(path, newPath, cancellationToken);

    public Task DeleteAsync(RemoteEntry entry, CancellationToken cancellationToken) =>
        entry.IsDirectory
            ? _client.DeleteDirectory(entry.FullPath, cancellationToken) // with its contents
            : _client.DeleteFile(entry.FullPath, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_client.IsConnected)
                await _client.Disconnect();
        }
        catch (Exception ex) when (ex is IOException or FtpException or ObjectDisposedException or TimeoutException)
        {
            // Closing anyway.
        }

        _client.Dispose();
    }

    private static bool TrustCertificate(FtpOptions options, X509Certificate certificate, SslPolicyErrors errors, string? errorMessage,
                                         IFileTransferPrompts prompts, ConnectionMemory memory, out string? failure, out bool cancelled)
    {
        failure = null;
        cancelled = false;
        if (errors == SslPolicyErrors.None)
            return true;

        using X509Certificate2 full = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        CertificateQuestion question = new(options.Host, options.Port, full, string.IsNullOrWhiteSpace(errorMessage) ? errors.ToString() : errorMessage);
        if (memory.TrustedOnce.ContainsKey(question.Fingerprint))
            return true;
        if (prompts.ConfirmCertificate(question) == TrustDecision.Reject)
        {
            failure = $"The certificate of {options.Host} was not trusted ({question.Problems}).";
            cancelled = true;
            return false;
        }

        memory.TrustedOnce[question.Fingerprint] = true;
        return true;
    }
}
