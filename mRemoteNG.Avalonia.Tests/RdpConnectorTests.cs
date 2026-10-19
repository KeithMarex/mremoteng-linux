using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using mRemoteNG.Avalonia.Services;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Avalonia.Sessions.Rdp;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Connection.Protocol.RDP;
using mRemoteNG.FileTransfer;
using AuthenticationLevel = mRemoteNG.Connection.Protocol.RDP.AuthenticationLevel;
using ProtocolType = mRemoteNG.Connection.Protocol.ProtocolType;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class RdpConnectorTests
{
    private static readonly RdpDisplay Display = new(4194322, 1000, 700, 1920, 1080);

    private FakeDialogService _dialogs = null!;
    private AppSettings _settings = null!;
    private readonly List<FreeRdpPlan> _started = [];
    private readonly List<(string Host, int Port)> _probed = [];
    private Func<string, int, ServerCertificate?> _server = null!;
    private Func<string, int, ServerCertificate> _gateway = null!;

    [SetUp]
    public void Setup()
    {
        _dialogs = new FakeDialogService();
        _settings = new AppSettings();
        _started.Clear();
        _probed.Clear();
        _server = (_, _) => throw new AssertionException("no server certificate expected");
        _gateway = (_, _) => throw new AssertionException("no gateway certificate expected");
    }

    [Test]
    public async Task NoAuthenticationConnectsWhateverTheCertificate()
    {
        RdpSession session = await ConnectAsync(Rdp(AuthenticationLevel.NoAuth));

        Assert.That(session.Plan.Arguments, Does.Contain("/cert:ignore"));
        Assert.That(_started.Single(), Is.SameAs(session.Plan));
    }

    [Test]
    public async Task RequiredAuthenticationOnlyAcceptsTrustedCertificates()
    {
        RdpSession session = await ConnectAsync(Rdp(AuthenticationLevel.AuthRequired));

        Assert.That(session.Plan.Arguments, Does.Contain("/cert:deny"));
    }

    [Test]
    public async Task ATrustedCertificateIsAcceptedWithoutAsking()
    {
        X509Certificate2 certificate = Certificate();
        _server = (_, _) => new ServerCertificate(certificate, SslPolicyErrors.None);

        RdpSession session = await ConnectAsync(Rdp(AuthenticationLevel.WarnOnFailedAuth));

        Assert.That(session.Plan.Arguments, Does.Contain("/cert:fingerprint:sha256:" + Hex(certificate)));
        Assert.That(_dialogs.TrustQuestions, Is.Empty);
        Assert.That(_probed.Single(), Is.EqualTo(("srv1", 3389)));
    }

    [Test]
    public async Task AnUntrustedCertificateIsAskedAboutAndCanBeRemembered()
    {
        X509Certificate2 certificate = Certificate();
        _server = (_, _) => new ServerCertificate(certificate, SslPolicyErrors.RemoteCertificateChainErrors);
        _dialogs.TrustAnswer = TrustDecision.TrustAndRemember;

        RdpSession first = await ConnectAsync(Rdp(AuthenticationLevel.WarnOnFailedAuth));
        RdpSession second = await ConnectAsync(Rdp(AuthenticationLevel.WarnOnFailedAuth));

        Assert.That(_dialogs.TrustQuestions.Single(), Does.Contain("srv1:3389").And.Contain("not issued by an authority"));
        Assert.That(first.Plan.Arguments, Does.Contain("/cert:fingerprint:sha256:" + Hex(certificate)));
        Assert.That(second.Plan.Arguments, Does.Contain("/cert:fingerprint:sha256:" + Hex(certificate)));
    }

    [Test]
    public async Task ARejectedCertificateStopsTheConnection()
    {
        _server = (_, _) => new ServerCertificate(Certificate(), SslPolicyErrors.RemoteCertificateNameMismatch);
        _dialogs.TrustAnswer = TrustDecision.Reject;

        await Assert.ThrowsAsync<SessionException>(() => ConnectAsync(Rdp(AuthenticationLevel.WarnOnFailedAuth)));
        Assert.That(_started, Is.Empty);
    }

    [Test]
    public async Task ACertificateThatCannotBeReadIsLeftToXfreerdp()
    {
        _server = (_, _) => throw new SocketException((int)SocketError.ConnectionRefused);

        RdpSession session = await ConnectAsync(Rdp(AuthenticationLevel.WarnOnFailedAuth));

        Assert.That(session.Plan.Arguments, Does.Contain("/cert:deny"));
        Assert.That(session.Plan.Warnings, Has.Some.Contains("could not be read"));
    }

    [Test]
    public async Task BehindAGatewayTheGatewaysCertificateIsCheckedAndTheComputersTrustedOnFirstUse()
    {
        X509Certificate2 gateway = Certificate();
        _gateway = (host, port) => host == "gw.example.com" && port == 443
            ? new ServerCertificate(gateway, SslPolicyErrors.None)
            : throw new AssertionException("wrong gateway");
        _server = (_, _) => throw new SocketException((int)SocketError.TimedOut);
        ConnectionInfo connection = Rdp(AuthenticationLevel.WarnOnFailedAuth);
        connection.RDGatewayUsageMethod = RDGatewayUsageMethod.Always;
        connection.RDGatewayHostname = "gw.example.com";

        RdpSession session = await ConnectAsync(connection);

        Assert.That(session.Plan.Arguments, Does.Contain($"/cert:fingerprint:sha256:{Hex(gateway)},tofu"));
        Assert.That(session.Plan.Warnings, Has.None.Contains("could not be read"));
    }

    [Test]
    public async Task AMissingPasswordIsAskedForWithNetworkLevelAuthentication()
    {
        ConnectionInfo connection = Rdp(AuthenticationLevel.NoAuth);
        connection.Password = string.Empty;
        _dialogs.Logins.Enqueue(new LoginCredentials("admin", "CORP", "typed"));

        RdpSession session = await ConnectAsync(connection);

        Assert.That(_dialogs.LoginQuestions.Single().Initial, Is.EqualTo(new LoginCredentials("admin", "", "")));
        Assert.That(session.Plan.Arguments, Is.SupersetOf(new[] { "/u:admin", "/d:CORP", "/p:typed" }));
        Assert.That(connection.Password, Is.Empty);
    }

    [Test]
    public async Task CancellingTheLoginStopsTheConnection()
    {
        ConnectionInfo connection = Rdp(AuthenticationLevel.NoAuth);
        connection.Password = string.Empty;

        await Assert.ThrowsAsync<SessionException>(() => ConnectAsync(connection));
        Assert.That(_started, Is.Empty);
    }

    [Test]
    public async Task WithoutNetworkLevelAuthenticationTheServerAsksForTheLogin()
    {
        ConnectionInfo connection = Rdp(AuthenticationLevel.NoAuth);
        connection.Password = string.Empty;
        connection.UseCredSsp = false;

        await ConnectAsync(connection);

        Assert.That(_dialogs.LoginQuestions, Is.Empty);
    }

    [Test]
    public async Task WithoutFreeRdpTheErrorSaysWhatToInstall()
    {
        RdpConnector connector = Connector(new SessionPrograms { Find = _ => null });

        SessionException error = (await Assert.ThrowsAsync<SessionException>(() =>
            connector.ConnectAsync(Rdp(AuthenticationLevel.NoAuth), Display, CancellationToken.None)))!;

        Assert.That(error.Message, Does.Contain("freerdp3-x11"));
    }

    [Test]
    public void AnOlderXfreerdpIsNotUsed()
    {
        SessionPrograms programs = new() { Find = program => program == "xfreerdp" ? "/usr/bin/xfreerdp" : null };

        Assert.Throws<SessionException>(() => FreeRdpProgram.Locate(programs, _ => 2));
        Assert.That(FreeRdpProgram.Locate(programs, _ => 3), Is.EqualTo("/usr/bin/xfreerdp"));
    }

    private Task<RdpSession> ConnectAsync(ConnectionInfo connection) =>
        Connector(new SessionPrograms { Find = program => "/usr/bin/" + program }).ConnectAsync(connection, Display, CancellationToken.None);

    private RdpConnector Connector(SessionPrograms programs) => new(programs, _dialogs, new CertificatePins(_settings, () => { }),
        (host, port, _) =>
        {
            _probed.Add((host, port));
            return Task.FromResult(_server(host, port));
        },
        (host, port, _) => Task.FromResult(_gateway(host, port)),
        plan =>
        {
            _started.Add(plan);
            return new FakeRdpProcess();
        });

    private static ConnectionInfo Rdp(AuthenticationLevel level)
    {
        ConnectionInfo connection = new()
        {
            Name = "Server 1", Protocol = ProtocolType.RDP, Hostname = "srv1", Username = "admin", Password = "secret",
            RDPAuthenticationLevel = level, UseCredSsp = true, RDGatewayUsageMethod = RDGatewayUsageMethod.Never
        };
        connection.SetDefaultPort();
        return connection;
    }

    internal static X509Certificate2 Certificate()
    {
        using RSA key = RSA.Create(2048);
        CertificateRequest request = new("CN=srv1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(30));
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
    }

    private static string Hex(X509Certificate2 certificate) => Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256));
}

/// <summary>Stands in for xfreerdp: records Stop and lets a test end it.</summary>
internal sealed class FakeRdpProcess : IRdpProcess
{
    public event EventHandler<RdpExit>? Exited;

    public bool Stopped { get; private set; }

    public bool Disposed { get; private set; }

    public void Stop() => Stopped = true;

    public void Dispose() => Disposed = true;

    public void Exit(int exitCode, string? lastError = null) => Exited?.Invoke(this, new RdpExit(exitCode, lastError));
}
