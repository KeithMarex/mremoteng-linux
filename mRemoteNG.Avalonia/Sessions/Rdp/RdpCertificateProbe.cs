using System.Buffers.Binary;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace mRemoteNG.Avalonia.Sessions.Rdp;

/// <summary>A server's TLS certificate, and what this computer thinks of it.</summary>
public sealed record ServerCertificate(X509Certificate2 Certificate, SslPolicyErrors Errors);

/// <summary>
/// Reads the TLS certificate of an RDP server or gateway before xfreerdp connects, so the app can ask the user about
/// it the way it asks about SFTP host keys and FTPS certificates; xfreerdp then accepts exactly that certificate.
/// </summary>
internal static class RdpCertificateProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // TPKT header, X.224 Connection Request, RDP Negotiation Request for TLS, CredSSP and CredSSP with Early User
    // Authorization (MS-RDPBCGR 2.2.1.1).
    private static readonly byte[] ConnectionRequest =
    [
        0x03, 0x00, 0x00, 0x13,
        0x0E, 0xE0, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x08, 0x00, 0x0B, 0x00, 0x00, 0x00
    ];

    private const byte NegotiationResponse = 0x02;

    /// <summary>The certificate of the RDP server; null when it does not use TLS (standard RDP security).</summary>
    public static async Task<ServerCertificate?> ReadRdpAsync(string host, int port, CancellationToken cancellation)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(Timeout);
        using TcpClient client = new();
        await client.ConnectAsync(host, port, timeout.Token);
        NetworkStream network = client.GetStream();
        await network.WriteAsync(ConnectionRequest, timeout.Token);

        byte[] header = new byte[4];
        await network.ReadExactlyAsync(header, timeout.Token);
        int length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
        if (header[0] != 0x03 || length < 11 || length > 1024)
            throw new IOException(UiText.NotAnRdpServer);
        byte[] confirm = new byte[length - 4];
        await network.ReadExactlyAsync(confirm, timeout.Token);

        // X.224 Connection Confirm (7 bytes), then the RDP Negotiation Response with the chosen protocol; without it,
        // or with protocol 0, the server uses standard RDP security and has no TLS certificate.
        if ((confirm[1] & 0xF0) != 0xD0)
            throw new IOException(UiText.NotAnRdpServer);
        if (confirm.Length < 15 || confirm[7] != NegotiationResponse || BinaryPrimitives.ReadUInt32LittleEndian(confirm.AsSpan(11)) == 0)
            return null;

        return await HandshakeAsync(network, host, timeout.Token);
    }

    /// <summary>The certificate of a TLS server, such as a Remote Desktop Gateway.</summary>
    public static async Task<ServerCertificate> ReadTlsAsync(string host, int port, CancellationToken cancellation)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(Timeout);
        using TcpClient client = new();
        await client.ConnectAsync(host, port, timeout.Token);
        return await HandshakeAsync(client.GetStream(), host, timeout.Token);
    }

    private static async Task<ServerCertificate> HandshakeAsync(Stream network, string host, CancellationToken cancellation)
    {
        ServerCertificate? seen = null;
        await using SslStream tls = new(network, leaveInnerStreamOpen: true);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = host,
            // Only looking: whatever the certificate, the handshake goes on so it can be shown to the user.
            RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
            {
                if (certificate is not null)
                    seen = new ServerCertificate(new X509Certificate2(certificate), errors);
                return true;
            }
        }, cancellation);
        return seen ?? throw new IOException(UiText.NoServerCertificate);
    }
}
