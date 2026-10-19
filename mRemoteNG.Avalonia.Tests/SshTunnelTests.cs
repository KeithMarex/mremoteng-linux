using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Avalonia.Sessions.Askpass;
using mRemoteNG.Avalonia.Sessions.Rdp;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol.RDP;
using mRemoteNG.Container;
using mRemoteNG.FileTransfer;
using mRemoteNG.FileTransfer.Sftp;
using NUnit.Framework;
using AuthenticationLevel = mRemoteNG.Connection.Protocol.RDP.AuthenticationLevel;
using ProtocolType = mRemoteNG.Connection.Protocol.ProtocolType;

namespace mRemoteNG.Avalonia.Tests;

/// <summary>Connections through an SSH tunnel (their SSH Tunnel setting), as in the Windows app.</summary>
public class SshTunnelTests
{
    private TempDirectory _directory = null!;
    private FakeDialogService _dialogs = null!;
    private List<ConnectionInfo> _tree = null!;
    private List<FakeTunnelProcess> _started = null!;
    private SshTunnels _tunnels = null!;

    [SetUp]
    public void Setup()
    {
        _directory = new TempDirectory();
        _dialogs = new FakeDialogService();
        _started = [];
        _tree =
        [
            new ContainerInfo { Name = "jump" },
            new ConnectionInfo { Name = "jump", Protocol = ProtocolType.Telnet, Hostname = "not-this-one" },
            new ConnectionInfo { Name = "jump", Protocol = ProtocolType.SSH2, Hostname = "bastion.example", Port = 22, Username = "me" }
        ];
        SessionPrograms programs = new() { Find = program => "/usr/bin/" + program };
        _tunnels = new SshTunnels(programs, _directory.File("run"), _dialogs, action => action(), () => _tree)
        {
            StartProcess = (plan, _) =>
            {
                FakeTunnelProcess process = new(plan);
                _started.Add(process);
                return process;
            }
        };
    }

    [TearDown]
    public void Teardown()
    {
        _tunnels.Dispose();
        foreach (FakeTunnelProcess process in _started)
            process.Stop();
        _directory.Dispose();
    }

    [Test]
    public async Task TheSessionConnectsToTheLocalEndOfATunnelToItsServer()
    {
        ConnectionInfo target = Target(ProtocolType.RDP, "inside", 3389);

        using TunnelLease lease = await _tunnels.OpenAsync(target, CancellationToken.None);

        FakeTunnelProcess ssh = _started.Single();
        int localPort = ssh.LocalPort;
        Assert.That(ssh.Plan.Arguments, Is.EqualTo(new[]
        {
            "-l", "me", "-N", "-o", "ExitOnForwardFailure=yes", "-L", $"127.0.0.1:{localPort}:inside:3389", "--", "bastion.example"
        }), "the SSH connection named, not the folder or the Telnet connection");
        Assert.That(lease.Target.Hostname, Is.EqualTo("127.0.0.1"));
        Assert.That(lease.Target.Port, Is.EqualTo(localPort));
        Assert.That(lease.Target.SSHTunnelConnectionName, Is.Empty);
        Assert.That(lease.Target.Name, Is.EqualTo("inside"));
        Assert.That(TunnelLease.ServerOf(lease.Target), Is.EqualTo(new TunnelledServer("inside", 3389, "jump")));
        Assert.That(target.Hostname, Is.EqualTo("inside"), "the connection itself is not changed");

        lease.Dispose();
        Assert.That(ssh.Stopped, Is.True);
    }

    [Test]
    public async Task AWebAddressGoesToTheTunnelWithItsPath()
    {
        ConnectionInfo target = Target(ProtocolType.HTTPS, "https://intranet.example/app?x=1", 443);

        using TunnelLease lease = await _tunnels.OpenAsync(target, CancellationToken.None);

        int localPort = _started.Single().LocalPort;
        Assert.That(lease.Target.Hostname, Is.EqualTo($"https://127.0.0.1:{localPort}/app?x=1"));
        Assert.That(_started.Single().Plan.Arguments, Does.Contain($"127.0.0.1:{localPort}:intranet.example:443"));
    }

    [Test]
    public void AnIPv6ServerIsWrittenInBrackets()
    {
        SessionPlan plan = SshTunnels.Forwarding(new SessionPlan("/usr/bin/ssh", ["--", "jump"]), 4000, "fd00::5", 22);

        Assert.That(plan.Arguments, Does.Contain("127.0.0.1:4000:[fd00::5]:22"));
    }

    [TestCase(ProtocolType.RDP, "srv", true)]
    [TestCase(ProtocolType.AnyDesk, "123456789", false)]
    [TestCase(ProtocolType.Terminal, "localhost", false)]
    [TestCase(ProtocolType.Terminal, "srv", true)]
    public void OnlyConnectionsWithAServerToReachGoThroughTheTunnel(ProtocolType protocol, string host, bool uses)
    {
        Assert.That(SshTunnels.Uses(Target(protocol, host, 22)), Is.EqualTo(uses));
        Assert.That(SshTunnels.Uses(new ConnectionInfo { Protocol = protocol, Hostname = host }), Is.False, "no tunnel set");
    }

    [Test]
    public async Task AnUnknownTunnelIsReported()
    {
        ConnectionInfo target = Target(ProtocolType.RDP, "inside", 3389);
        target.SSHTunnelConnectionName = "Jump";

        SessionException? error = await Assert.ThrowsAsync<SessionException>(() => _tunnels.OpenAsync(target, CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("\"Jump\""), "names are exact, as on Windows");
        Assert.That(_started, Is.Empty);
    }

    [Test]
    public async Task FtpCannotGoThroughATunnel()
    {
        SessionException? error = await Assert.ThrowsAsync<SessionException>(() => _tunnels.OpenAsync(Target(ProtocolType.FTP, "files", 21), CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("SFTP"));
    }

    [Test]
    public async Task WhenSshGivesUpItsReasonIsShown()
    {
        _tunnels.StartProcess = (plan, _) =>
        {
            FakeTunnelProcess process = new(plan, listen: false) { HasExited = true, LastError = "me@bastion.example: Permission denied (publickey)." };
            _started.Add(process);
            return process;
        };

        SessionException? error = await Assert.ThrowsAsync<SessionException>(() => _tunnels.OpenAsync(Target(ProtocolType.RDP, "inside", 3389), CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("Permission denied (publickey)"));
    }

    [Test]
    public async Task ATunnelThatDoesNotOpenInTimeIsStopped()
    {
        _tunnels.ReadyTimeout = TimeSpan.FromMilliseconds(300);
        _tunnels.StartProcess = (plan, _) =>
        {
            FakeTunnelProcess process = new(plan, listen: false);
            _started.Add(process);
            return process;
        };

        SessionException? error = await Assert.ThrowsAsync<SessionException>(() => _tunnels.OpenAsync(Target(ProtocolType.RDP, "inside", 3389), CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("did not become available in time"));
        Assert.That(_started.Single().Stopped, Is.True);
    }

    [Test]
    public async Task AnSshServerBehindTheTunnelIsReachedUnderItsOwnNameAndHostKey()
    {
        using TunnelLease lease = await _tunnels.OpenAsync(Target(ProtocolType.SSH2, "inside", 2200), CancellationToken.None);
        int localPort = lease.Target.Port;

        SessionPlan plan = OpenSshCommand.Build(lease.Target, "/usr/bin/ssh");

        Assert.That(plan.Arguments, Is.EqualTo(new[]
        {
            "-p", localPort.ToString(), "-l", "admin", "-o", "HostName=127.0.0.1", "-o", "HostKeyAlias=[inside]:2200", "--", "inside"
        }));
        Assert.That(plan.PasswordHost, Is.EqualTo("[inside]:2200"), "ssh names the alias in its password prompt");
        Assert.That(OpenSshCommand.IsPasswordPromptFor("admin@[inside]:2200's password: ", plan.PasswordHost), Is.True);
    }

    [Test]
    public void UnderAHostKeyAliasKnownHostsNamesNoPort()
    {
        Assert.That(new SftpOptions { Host = "127.0.0.1", Port = 40123, User = "me", HostKeyAlias = "inside" }.KnownHostsPort, Is.EqualTo(22));
        Assert.That(new SftpOptions { Host = "inside", Port = 2200, User = "me" }.KnownHostsPort, Is.EqualTo(2200));
    }

    [Test]
    public async Task QuestionsSshAsksWithoutATerminalComeUpAsDialogs()
    {
        // As the helper would ask for a tunnel's ssh: a host key to accept, then a password that is not stored.
        using AskpassServer server = AskpassServer.Start(_directory.File("ask"), "/usr/lib/mremoteng/mRemoteNG.Avalonia", null, "bastion.example",
                                                         prompt => Task.FromResult<string?>(prompt.Contains("yes/no") ? "yes" : "typed"));
        string token = server.Environment[AskpassServer.TokenVariable];

        AskpassReply hostKey = await server.AnswerAsync(new AskpassRequest(token, "Are you sure you want to continue connecting (yes/no/[fingerprint])?"), CancellationToken.None);
        AskpassReply password = await server.AnswerAsync(new AskpassRequest(token, "me@bastion.example's password: "), CancellationToken.None);
        AskpassReply stranger = await server.AnswerAsync(new AskpassRequest("wrong", "me@bastion.example's password: "), CancellationToken.None);

        Assert.That(hostKey, Is.EqualTo(new AskpassReply("yes", false)));
        Assert.That(password, Is.EqualTo(new AskpassReply("typed", false)));
        Assert.That(stranger.AskInTerminal, Is.True, "nothing for a helper that is not this session's");
    }

    private static ConnectionInfo Target(ProtocolType protocol, string host, int port) => new()
    {
        Name = host.Contains("://") ? "intranet" : host, Protocol = protocol, Hostname = host, Port = port, Username = "admin",
        SSHTunnelConnectionName = "jump"
    };
}

/// <summary>Sessions through a tunnel: certificates are the server's, and the tunnel closes with the session.</summary>
public class SshTunnelSessionTests
{
    [Test]
    public async Task AnRdpCertificateReadThroughATunnelIsTheServers()
    {
        System.Security.Cryptography.X509Certificates.X509Certificate2 certificate = RdpConnectorTests.Certificate(); // CN=srv1
        AppSettings settings = new();
        CertificatePins pins = new(settings, () => { });
        pins.Trust("srv1", 3389, new CertificateQuestion("srv1", 3389, certificate, string.Empty).Fingerprint);
        FakeDialogService dialogs = new();
        List<(string Host, int Port)> probed = [];
        RdpConnector connector = new(new SessionPrograms { Find = program => "/usr/bin/" + program }, dialogs, pins,
            (host, port, _) =>
            {
                probed.Add((host, port));
                // Read from 127.0.0.1, the name does not match.
                return Task.FromResult<ServerCertificate?>(new ServerCertificate(certificate,
                    SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors));
            },
            (_, _, _) => throw new AssertionException("no gateway"),
            _ => new FakeRdpProcess());
        ConnectionInfo connection = new()
        {
            Name = "Server 1", Protocol = ProtocolType.RDP, Hostname = "127.0.0.1", Port = 40100, Username = "admin", Password = "secret",
            RDPAuthenticationLevel = AuthenticationLevel.WarnOnFailedAuth, UseCredSsp = true, RDGatewayUsageMethod = RDGatewayUsageMethod.Never
        };
        using TunnelLease lease = new(connection, new TunnelledServer("srv1", 3389, "jump"), new FakeTunnelProcess(new SessionPlan("ssh", []), listen: false), null);

        RdpSession session = await connector.ConnectAsync(connection, new RdpDisplay(1, 800, 600, 1920, 1080), CancellationToken.None);

        Assert.That(probed.Single(), Is.EqualTo(("127.0.0.1", 40100)));
        Assert.That(dialogs.TrustQuestions, Is.Empty, "trusted for srv1:3389, and its name is srv1");
        Assert.That(session.Plan.Arguments, Has.Some.StartsWith("/cert:fingerprint:sha256:"));
    }

    [Test]
    public async Task TheTunnelClosesWithTheFileTransfer()
    {
        using TempDirectory directory = new();
        FakeTunnelProcess? ssh = null;
        List<ConnectionInfo> tree = [new() { Name = "jump", Protocol = ProtocolType.SSH2, Hostname = "bastion.example", Port = 22 }];
        SshTunnels tunnels = new(new SessionPrograms { Find = program => "/usr/bin/" + program }, directory.File("run"), new FakeDialogService(),
                                 action => action(), () => tree)
        {
            StartProcess = (plan, _) => ssh = new FakeTunnelProcess(plan)
        };
        ConnectionInfo connection = new() { Name = "files", Protocol = ProtocolType.SFTP, Hostname = "inside", Port = 22, SSHTunnelConnectionName = "jump" };
        ConnectionInfo? prepared = null;
        FakeDialogService dialogs = new();
        using FileTransferViewModel tab = new(connection, (target, _) =>
            {
                prepared = target;
                return Task.FromResult(new FileTransferTarget(null, [], (_, _, _) => Task.FromResult<IRemoteFileSystem>(new FileTransfer.Tests.FakeRemoteFileSystem())));
            },
            dialogs, new FileTransferPrompts(dialogs, new CertificatePins(new AppSettings(), () => { })), action => action(),
            new AppSettings(), () => { }) { Tunnels = tunnels };

        await tab.ConnectAsync();

        Assert.That(tab.State, Is.EqualTo(FileTransferState.Connected));
        Assert.That(prepared!.Hostname, Is.EqualTo("127.0.0.1"));
        Assert.That(prepared.Port, Is.EqualTo(ssh!.LocalPort));
        Assert.That(ssh.Stopped, Is.False);

        tab.Disconnect();
        Assert.That(ssh.Stopped, Is.True);
    }
}

/// <summary>Stands in for the ssh of a tunnel: listens on the local port it forwards, as ssh does once it is connected.</summary>
internal sealed class FakeTunnelProcess : ITunnelProcess
{
    private readonly TcpListener? _listener;

    public FakeTunnelProcess(SessionPlan plan, bool listen = true)
    {
        Plan = plan;
        int index = plan.Arguments.ToList().IndexOf("-L");
        LocalPort = index < 0 ? 0 : int.Parse(plan.Arguments[index + 1].Split(':')[1]);
        if (listen && LocalPort > 0)
        {
            _listener = new TcpListener(IPAddress.Loopback, LocalPort);
            _listener.Start();
        }
    }

    public SessionPlan Plan { get; }

    public int LocalPort { get; }

    public bool Stopped { get; private set; }

    public bool HasExited { get; set; }

    public string? LastError { get; set; }

    public event EventHandler? Exited;

    public void Stop()
    {
        Stopped = true;
        _listener?.Stop();
        if (!HasExited)
        {
            HasExited = true;
            Exited?.Invoke(this, EventArgs.Empty);
        }
    }
}
