using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using mRemoteNG.Avalonia.Services;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Avalonia.Sessions.Rdp;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Connection;
using mRemoteNG.FileTransfer;
using NUnit.Framework;
using ProtocolType = mRemoteNG.Connection.Protocol.ProtocolType;

namespace mRemoteNG.Avalonia.Tests;

/// <summary>SPICE consoles: Proxmox VE virtual machines through the Proxmox API, and QEMU's own SPICE port.</summary>
public class SpiceConnectorTests
{
    private TempDirectory _directory = null!;
    private FakeDialogService _dialogs = null!;
    private AppSettings _settings = null!;
    private FakeProxmox _proxmox = null!;
    private SslPolicyErrors _certificateErrors;
    private readonly List<(SessionPlan Plan, string File)> _started = [];
    private TaskCompletionSource<int> _viewer = null!;
    private Func<X509Certificate2, bool>? _accepts;

    [SetUp]
    public void Setup()
    {
        _directory = new TempDirectory();
        _dialogs = new FakeDialogService();
        _settings = new AppSettings();
        _proxmox = new FakeProxmox();
        _certificateErrors = SslPolicyErrors.None;
        _started.Clear();
        _viewer = new TaskCompletionSource<int>();
    }

    [TearDown]
    public void Teardown() => _directory.Dispose();

    [Test]
    public async Task ADirectConsoleGoesToQemuWithThePasswordInTheViewersFileOnly()
    {
        ConnectionInfo connection = Spice("kvm1", 5901, SpiceSource.Direct);
        connection.Password = "s3c\\ret";

        (SessionPlan plan, Task<int> exited) = await Connector().LaunchAsync(connection, CancellationToken.None);

        (SessionPlan started, string file) = _started.Single();
        Assert.That(started, Is.SameAs(plan));
        Assert.That(plan.Program, Is.EqualTo("/usr/bin/remote-viewer"));
        Assert.That(plan.Arguments, Is.EqualTo(new[] { file }));
        Assert.That(plan.ToString(), Does.Not.Contain("s3c"));
        Assert.That(File.ReadAllText(file), Is.EqualTo(
            "[virt-viewer]\ntype=spice\nhost=kvm1\nport=5901\npassword=s3c\\\\ret\ntitle=Web server\ndelete-this-file=1\n"));
        Assert.That(File.GetUnixFileMode(file), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));

        _viewer.SetResult(0);
        Assert.That(await exited, Is.Zero);
        Assert.That(File.Exists(file), Is.False, "gone once remote-viewer is done");
    }

    [Test]
    public async Task ALineBreakCannotAddSettingsToTheViewersFile()
    {
        ConnectionInfo connection = Spice("kvm1", 5901, SpiceSource.Direct);
        connection.Password = "pw\nproxy=http://evil:3128";

        await Assert.ThrowsAsync<SessionException>(() => Connector().LaunchAsync(connection, CancellationToken.None));
        Assert.That(_started, Is.Empty);
    }

    [Test]
    public async Task AProxmoxVmIsFoundByNameAndOpenedWithAnApiToken()
    {
        ConnectionInfo connection = Spice("pve.example", 8006, SpiceSource.Proxmox, vm: "web01");
        connection.Username = "root@pam!mrx";
        connection.Password = "0b1d-token";

        await Connector().LaunchAsync(connection, CancellationToken.None);

        Assert.That(_proxmox.Requests.Select(request => $"{request.Method} {request.Path}"), Is.EqualTo(new[]
        {
            "GET /api2/json/cluster/resources?type=vm", "POST /api2/json/nodes/pve2/qemu/105/spiceproxy"
        }));
        Assert.That(_proxmox.Requests.Select(request => request.Authorization), Has.All.EqualTo("PVEAPIToken=root@pam!mrx=0b1d-token"));
        Assert.That(_proxmox.Requests[1].Body, Is.EqualTo("proxy=pve.example"), "the address this computer reaches Proxmox at");
        Assert.That(File.ReadAllText(_started.Single().File), Is.EqualTo(
            "[virt-viewer]\ntype=spice\nhost=pvespiceproxy:65f0:qemu:105\ntls-port=61000\npassword=one-time\n" +
            "proxy=http://pve.example:3128\nca=-----BEGIN CERTIFICATE-----\\nMIIB\\n-----END CERTIFICATE-----\\n\n" +
            "title=VM 105 - web01\ndelete-this-file=1\n"));
        Assert.That(_dialogs.TrustQuestions, Is.Empty);
    }

    [Test]
    public async Task AUserLogsInForATicket()
    {
        ConnectionInfo connection = Spice("pve.example", 8006, SpiceSource.Proxmox, vm: "105", node: "pve2");
        connection.Username = "admin";
        connection.Password = "pw";

        await Connector().LaunchAsync(connection, CancellationToken.None);

        Assert.That(_proxmox.Requests.Select(request => $"{request.Method} {request.Path}"), Is.EqualTo(new[]
        {
            "POST /api2/json/access/ticket", "POST /api2/json/nodes/pve2/qemu/105/spiceproxy"
        }), "an ID and a node need no search");
        Assert.That(_proxmox.Requests[0].Body, Is.EqualTo("username=admin%40pam&password=pw"), "Proxmox's default realm");
        Assert.That(_proxmox.Requests[1].Cookie, Is.EqualTo("PVEAuthCookie=PVE%3Aadmin%40pam%3A1234"));
        Assert.That(_proxmox.Requests[1].Csrf, Is.EqualTo("csrf-1"));
    }

    [Test]
    public async Task AMissingLoginIsAskedFor()
    {
        ConnectionInfo connection = Spice("pve.example", 8006, SpiceSource.Proxmox, vm: "105", node: "pve2");
        _dialogs.Logins.Enqueue(new LoginCredentials("admin@pve", string.Empty, "typed"));

        await Connector().LaunchAsync(connection, CancellationToken.None);

        Assert.That(_dialogs.LoginQuestions.Single().Prompt, Does.Contain("pve.example"));
        Assert.That(_proxmox.Requests[0].Body, Is.EqualTo("username=admin%40pve&password=typed"));
    }

    [Test]
    public async Task TwoFactorAuthenticationAsksForAnApiToken()
    {
        _proxmox.NeedsSecondFactor = true;
        ConnectionInfo connection = Spice("pve.example", 8006, SpiceSource.Proxmox, vm: "105", node: "pve2");
        connection.Username = "admin@pam";
        connection.Password = "pw";

        SessionException? error = await Assert.ThrowsAsync<SessionException>(() => Connector().LaunchAsync(connection, CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("API token"));
    }

    [Test]
    public async Task AnUntrustedCertificateIsAskedAboutAndARejectionStopsThere()
    {
        _certificateErrors = SslPolicyErrors.RemoteCertificateChainErrors;
        _dialogs.TrustAnswer = TrustDecision.Reject;

        await Assert.ThrowsAsync<SessionException>(() => Connector().LaunchAsync(Token(Spice("pve.example", 8006, SpiceSource.Proxmox, vm: "105")),
                                                                             CancellationToken.None));

        Assert.That(_dialogs.TrustQuestions.Single(), Does.Contain("pve.example:8006"));
        Assert.That(_proxmox.Requests, Is.Empty);
    }

    [Test]
    public async Task ACertificateTrustedOnceIsTheOnlyOneTheApiCallsAccept()
    {
        _certificateErrors = SslPolicyErrors.RemoteCertificateChainErrors;
        _dialogs.TrustAnswer = TrustDecision.TrustOnce;

        await Connector().LaunchAsync(Token(Spice("pve.example", 8006, SpiceSource.Proxmox, vm: "105", node: "pve2")), CancellationToken.None);

        Assert.That(_accepts!(Certificate), Is.True);
        Assert.That(_accepts(RdpConnectorTests.Certificate()), Is.False, "another certificate");
    }

    [TestCase("ct1", "is a container")]
    [TestCase("nothing", "has no virtual machine \"nothing\"")]
    public async Task AVmThatIsNotThereOrHasNoConsoleIsReported(string vm, string message)
    {
        SessionException? error = await Assert.ThrowsAsync<SessionException>(() =>
            Connector().LaunchAsync(Token(Spice("pve.example", 8006, SpiceSource.Proxmox, vm: vm)), CancellationToken.None));

        Assert.That(error!.Message, Does.Contain(message));
    }

    [Test]
    public async Task ProxmoxsReasonIsShown()
    {
        _proxmox.SpiceProxyError = "VM 105 not running";

        SessionException? error = await Assert.ThrowsAsync<SessionException>(() =>
            Connector().LaunchAsync(Token(Spice("pve.example", 8006, SpiceSource.Proxmox, vm: "105", node: "pve2")), CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("VM 105 not running"));
    }

    [Test]
    public async Task AMissingViewerNamesItsPackage()
    {
        SpiceConnector connector = new(new SessionPrograms { Find = _ => null }, _directory.File("run"), _dialogs,
                                       new CertificatePins(_settings, () => { }));

        SessionException? error = await Assert.ThrowsAsync<SessionException>(() =>
            connector.LaunchAsync(Spice("kvm1", 5900, SpiceSource.Direct), CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("virt-viewer"));
    }

    private static readonly X509Certificate2 Certificate = RdpConnectorTests.Certificate();

    private SpiceConnector Connector() => new(
        new SessionPrograms
        {
            Find = program => "/usr/bin/" + program,
            StartDetached = plan =>
            {
                _started.Add((plan, plan.Arguments[0]));
                return _viewer.Task;
            }
        },
        _directory.File("run"), _dialogs, new CertificatePins(_settings, () => { }),
        (_, _, _) => Task.FromResult(new ServerCertificate(Certificate, _certificateErrors)),
        accepts =>
        {
            _accepts = accepts;
            return _proxmox;
        });

    private static ConnectionInfo Spice(string host, int port, SpiceSource source, string? vm = null, string? node = null)
    {
        ConnectionInfo connection = new() { Name = "Web server", Protocol = ProtocolType.SPICE, Hostname = host, Port = port };
        connection.SetPluginProperty(SpiceSettings.SourceKey, source.ToString());
        connection.SetPluginProperty(SpiceSettings.VmKey, vm);
        connection.SetPluginProperty(SpiceSettings.NodeKey, node);
        return connection;
    }

    private static ConnectionInfo Token(ConnectionInfo connection)
    {
        connection.Username = "root@pam!mrx";
        connection.Password = "secret";
        return connection;
    }

    /// <summary>The parts of the Proxmox VE API a SPICE console needs.</summary>
    private sealed class FakeProxmox : HttpMessageHandler
    {
        public List<(string Method, string Path, string? Authorization, string? Cookie, string? Csrf, string Body)> Requests { get; } = [];

        public bool NeedsSecondFactor { get; set; }

        public string? SpiceProxyError { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            string path = request.RequestUri!.PathAndQuery;
            Requests.Add((request.Method.Method, path, Header(request, "Authorization"), Header(request, "Cookie"),
                          Header(request, "CSRFPreventionToken"), body));
            return path switch
            {
                "/api2/json/access/ticket" when NeedsSecondFactor =>
                    Json("""{"data":{"ticket":"PVE:admin@pam:TFA","CSRFPreventionToken":"csrf-1","NeedTFA":1}}"""),
                "/api2/json/access/ticket" =>
                    Json("""{"data":{"ticket":"PVE:admin@pam:1234","CSRFPreventionToken":"csrf-1","username":"admin@pam"}}"""),
                "/api2/json/cluster/resources?type=vm" => Json("""
                    {"data":[{"vmid":100,"name":"db01","node":"pve1","type":"qemu"},
                             {"vmid":105,"name":"web01","node":"pve2","type":"qemu"},
                             {"vmid":200,"name":"ct1","node":"pve1","type":"lxc"}]}
                    """),
                "/api2/json/nodes/pve2/qemu/105/spiceproxy" when SpiceProxyError is { } error =>
                    new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        ReasonPhrase = error, Content = new StringContent($$"""{"data":null,"message":"{{error}}\n"}""")
                    },
                "/api2/json/nodes/pve2/qemu/105/spiceproxy" => Json("""
                    {"data":{"type":"spice","host":"pvespiceproxy:65f0:qemu:105","tls-port":61000,"password":"one-time",
                             "proxy":"http://pve.example:3128","ca":"-----BEGIN CERTIFICATE-----\\nMIIB\\n-----END CERTIFICATE-----\\n",
                             "title":"VM 105 - web01","delete-this-file":1}}
                    """),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }

        private static string? Header(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out IEnumerable<string>? values) ? string.Join(";", values) : null;

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
