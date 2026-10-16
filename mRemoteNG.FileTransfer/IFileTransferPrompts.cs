using System.Security.Cryptography.X509Certificates;

namespace mRemoteNG.FileTransfer;

public enum TrustDecision
{
    Reject,
    TrustOnce,
    TrustAndRemember
}

/// <summary>An SSH host key that is not in known_hosts yet.</summary>
/// <param name="KnownKeyTypes">Other key types known_hosts has for this host (it is known, just not with this key type).</param>
public sealed record HostKeyQuestion(string Host, int Port, string KeyType, string Fingerprint, IReadOnlyList<string> KnownKeyTypes);

/// <summary>An FTPS certificate the system does not trust.</summary>
public sealed record CertificateQuestion(string Host, int Port, X509Certificate2 Certificate, string Problems)
{
    /// <summary>The certificate's SHA-256 fingerprint, as hex pairs ("AB:CD:...").</summary>
    public string Fingerprint => string.Join(':', Certificate.GetCertHash(System.Security.Cryptography.HashAlgorithmName.SHA256)
                                                             .Select(b => b.ToString("X2")));
}

/// <summary>
/// What a connection asks the user. The methods are called on a worker thread while connecting and may block until
/// the user answered; null means the user cancelled.
/// </summary>
public interface IFileTransferPrompts
{
    TrustDecision ConfirmHostKey(HostKeyQuestion question);

    TrustDecision ConfirmCertificate(CertificateQuestion question);

    string? AskPassword(string prompt);

    string? AskPassphrase(string keyFile);
}
