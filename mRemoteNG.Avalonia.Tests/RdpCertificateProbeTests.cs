using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using mRemoteNG.Avalonia.Sessions.Rdp;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class RdpCertificateProbeTests
{
    // X.224 Connection Confirm with an RDP Negotiation Response choosing TLS (protocol 1) or standard RDP security (0).
    private static readonly byte[] ConfirmTls = [0x03, 0x00, 0x00, 0x13, 0x0E, 0xD0, 0, 0, 0x12, 0x34, 0, 0x02, 0x00, 0x08, 0x00, 0x01, 0, 0, 0];
    private static readonly byte[] ConfirmRdpSecurity = [0x03, 0x00, 0x00, 0x0B, 0x06, 0xD0, 0, 0, 0x12, 0x34, 0];

    [Test]
    public async Task TheCertificateOfAnRdpServerIsRead()
    {
        using X509Certificate2 certificate = RdpConnectorTests.Certificate();
        using TcpListener listener = Listen();
        Task<byte[]> server = ServeRdpAsync(listener, ConfirmTls, certificate);

        ServerCertificate? read = await RdpCertificateProbe.ReadRdpAsync("127.0.0.1", Port(listener), CancellationToken.None);

        Assert.That(read!.Certificate.Thumbprint, Is.EqualTo(certificate.Thumbprint));
        Assert.That(read.Errors, Is.Not.EqualTo(SslPolicyErrors.None));
        // TLS, CredSSP and CredSSP with early user authorization were offered.
        Assert.That((await server)[15], Is.EqualTo(0x0B));
    }

    [Test]
    public async Task AServerWithStandardRdpSecurityHasNoCertificate()
    {
        using TcpListener listener = Listen();
        Task<byte[]> server = ServeRdpAsync(listener, ConfirmRdpSecurity, certificate: null);

        Assert.That(await RdpCertificateProbe.ReadRdpAsync("127.0.0.1", Port(listener), CancellationToken.None), Is.Null);
        await server;
    }

    [Test]
    public async Task SomethingElseThanAnRdpServerIsReported()
    {
        using TcpListener listener = Listen();
        _ = Task.Run(async () =>
        {
            using Socket connection = await listener.AcceptSocketAsync();
            connection.Send("SSH-2.0-OpenSSH_9.6\r\n"u8);
            connection.Shutdown(SocketShutdown.Both);
        });

        await Assert.ThrowsAsync<IOException>(() => RdpCertificateProbe.ReadRdpAsync("127.0.0.1", Port(listener), CancellationToken.None));
    }

    [Test]
    public async Task TheCertificateOfAGatewayIsRead()
    {
        using X509Certificate2 certificate = RdpConnectorTests.Certificate();
        using TcpListener listener = Listen();
        Task server = Task.Run(async () =>
        {
            using Socket connection = await listener.AcceptSocketAsync();
            await HandshakeAsync(new NetworkStream(connection, ownsSocket: false), certificate);
        });

        ServerCertificate read = await RdpCertificateProbe.ReadTlsAsync("127.0.0.1", Port(listener), CancellationToken.None);

        Assert.That(read.Certificate.Thumbprint, Is.EqualTo(certificate.Thumbprint));
        await server;
    }

    private static TcpListener Listen()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return listener;
    }

    private static int Port(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;

    // Answers the client's connection request, then does the TLS handshake if the answer chose TLS.
    private static Task<byte[]> ServeRdpAsync(TcpListener listener, byte[] confirm, X509Certificate2? certificate) => Task.Run(async () =>
    {
        using Socket connection = await listener.AcceptSocketAsync();
        NetworkStream stream = new(connection, ownsSocket: false);
        byte[] request = new byte[19];
        await stream.ReadExactlyAsync(request);
        await stream.WriteAsync(confirm);
        if (certificate is not null)
            await HandshakeAsync(stream, certificate);
        return request;
    });

    private static async Task HandshakeAsync(Stream stream, X509Certificate2 certificate)
    {
        await using SslStream tls = new(stream);
        try
        {
            await tls.AuthenticateAsServerAsync(certificate);
        }
        catch (IOException)
        {
            // The probe hangs up once it has the certificate.
        }
    }
}
