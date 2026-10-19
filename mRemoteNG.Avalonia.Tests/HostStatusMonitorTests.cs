using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Platform;
using mRemoteNG.Avalonia.Services;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Config.Settings;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Tree.Root;
using NUnit.Framework;
using ProtocolType = mRemoteNG.Connection.Protocol.ProtocolType;

namespace mRemoteNG.Avalonia.Tests;

/// <summary>View > Host Status: which servers in the tree answer.</summary>
public class HostStatusMonitorTests
{
    [TestCase(ProtocolType.SSH2, "srv", 0, "srv:22")]
    [TestCase(ProtocolType.RDP, " srv ", 3390, "srv:3390")]
    [TestCase(ProtocolType.HTTPS, "https://intranet.example:8443/app", 443, "intranet.example:8443")]
    [TestCase(ProtocolType.HTTP, "intranet", 80, "intranet:80")]
    [TestCase(ProtocolType.Terminal, "srv", 22, "srv:22")]
    [TestCase(ProtocolType.Terminal, "localhost", 22, null)]
    [TestCase(ProtocolType.PowerShell, "", 22, null)]
    [TestCase(ProtocolType.AnyDesk, "123456789", 0, null)]
    [TestCase(ProtocolType.IntApp, "srv", 22, null)]
    [TestCase(ProtocolType.SSH2, "", 22, null)]
    public void EachConnectionIsCheckedWhereItsServerListens(ProtocolType protocol, string host, int port, string? target)
    {
        ConnectionInfo connection = new() { Protocol = protocol, Hostname = host, Port = port };

        Assert.That(HostStatusMonitor.TargetOf(connection) is { } found ? $"{found.Host}:{found.Port}" : null, Is.EqualTo(target));
    }

    [Test]
    public void AConnectionThroughAnSshTunnelOrAFolderIsNotChecked()
    {
        ConnectionInfo tunnelled = new() { Protocol = ProtocolType.RDP, Hostname = "inside", Port = 3389, SSHTunnelConnectionName = "jump" };

        Assert.That(HostStatusMonitor.TargetOf(tunnelled), Is.Null);
        Assert.That(HostStatusMonitor.TargetOf(new ContainerInfo { Hostname = "srv" }), Is.Null);
    }

    [Test]
    public async Task EachServerIsCheckedOnceAndEveryNodeShowsTheResult()
    {
        (ConnectionNodeViewModel root, ConnectionNodeViewModel[] nodes) = Tree(("a", "srv"), ("b", "srv"), ("c", "down"));
        ConcurrentBag<string> checks = [];
        using HostStatusMonitor monitor = new()
        {
            Probe = (host, port, _) =>
            {
                checks.Add($"{host}:{port}");
                return Task.FromResult(host == "down" ? "Connection refused" : null);
            }
        };

        monitor.Start(() => root.SelfAndDescendants());
        await WaitFor(() => nodes.All(node => node.HasHostStatus));

        Assert.That(checks, Is.EquivalentTo(new[] { "srv:22", "down:22" }));
        Assert.That(nodes.Select(node => node.HostStatus), Is.EqualTo(new[] { HostStatus.Online, HostStatus.Online, HostStatus.Offline }));
        Assert.That(nodes[2].ToolTip, Does.Contain("Offline: Connection refused"));
        Assert.That(nodes[2].HostStatusIcon is null, Is.EqualTo(nodes[0].HostStatusIcon is null), "both have an icon where images load");
        Assert.That(root.HasHostStatus, Is.False, "folders show none");

        monitor.Stop();
        Assert.That(nodes.Select(node => node.HasHostStatus), Is.All.False);
        Assert.That(nodes[2].ToolTip, Does.Not.Contain("Offline"));
    }

    [Test]
    public async Task AtMost16ServersAreCheckedAtATime()
    {
        (ConnectionNodeViewModel root, ConnectionNodeViewModel[] nodes) =
            Tree(Enumerable.Range(0, 40).Select(i => ($"c{i}", $"host{i}")).ToArray());
        int running = 0, most = 0;
        using HostStatusMonitor monitor = new()
        {
            Probe = async (_, _, _) =>
            {
                int now = Interlocked.Increment(ref running);
                InterlockedMax(ref most, now);
                await Task.Delay(20);
                Interlocked.Decrement(ref running);
                return null;
            }
        };

        monitor.Start(() => root.SelfAndDescendants());
        await WaitFor(() => nodes.All(node => node.HasHostStatus));

        Assert.That(most, Is.EqualTo(HostStatusMonitor.MaxParallelChecks));
    }

    [Test]
    public async Task ARealServerIsOnlineAndAClosedPortOffline()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Assert.That(await HostStatusMonitor.ProbeAsync("127.0.0.1", port, CancellationToken.None), Is.Null);
        listener.Stop();
        Assert.That(await HostStatusMonitor.ProbeAsync("127.0.0.1", port, CancellationToken.None), Is.Not.Null);
    }

    internal static async Task WaitFor(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.That(condition(), Is.True);
    }

    private static (ConnectionNodeViewModel Root, ConnectionNodeViewModel[] Nodes) Tree(params (string Name, string Host)[] connections)
    {
        RootNodeInfo root = new(RootNodeType.Connection);
        foreach ((string name, string host) in connections)
            root.AddChild(new ConnectionInfo { Name = name, Protocol = ProtocolType.SSH2, Hostname = host, Port = 22 });
        ConnectionNodeViewModel node = new(root);
        return (node, node.Children.ToArray());
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = target) < value && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }
}

/// <summary>View > Host Status through the main window: remembered, and a changed host checked at once.</summary>
public class HostStatusViewTests
{
    private TempDirectory _directory = null!;
    private AppSettingsStore _store = null!;
    private MainWindowViewModel _viewModel = null!;
    private readonly ConcurrentBag<string> _checks = [];

    [SetUp]
    public async Task Setup()
    {
        _directory = new TempDirectory();
        AppPaths paths = new(_directory.File("config"), _directory.File("data"));
        AppSettings settings = new();
        CoreRuntime.Settings = settings.Core;
        _store = new AppSettingsStore(paths.SettingsFile);
        _viewModel = new MainWindowViewModel(settings, _store, paths, new FakeDialogService(), action => action());
        _viewModel.HostStatusChecks.Probe = (host, port, _) =>
        {
            _checks.Add($"{host}:{port}");
            return Task.FromResult<string?>(null);
        };
        await _viewModel.InitializeAsync();
        _viewModel.SelectedNode = _viewModel.RootNodes.Single();
        _viewModel.AddConnectionCommand.Execute(null);
    }

    [TearDown]
    public void Teardown()
    {
        _viewModel.HostStatusChecks.Dispose();
        _directory.Dispose();
        CoreRuntime.Settings = new CoreSettings();
    }

    [Test]
    public async Task TurningItOnChecksAndIsRemembered()
    {
        Assert.That(_viewModel.HostStatusChecks.IsRunning, Is.False, "off until the user turns it on");
        ConnectionNodeViewModel node = _viewModel.SelectedNode!;
        node.Connection.Hostname = "srv";

        _viewModel.IsHostStatusVisible = true;
        await HostStatusMonitorTests.WaitFor(() => node.HostStatus == HostStatus.Online);

        Assert.That(_store.Load().ShowHostStatus, Is.True);
        _viewModel.IsHostStatusVisible = false;
        Assert.That(node.HasHostStatus, Is.False);
        Assert.That(_store.Load().ShowHostStatus, Is.False);
    }

    [Test]
    public async Task AChangedHostIsCheckedAtOnce()
    {
        _viewModel.IsHostStatusVisible = true;
        ConnectionNodeViewModel node = _viewModel.SelectedNode!;
        Assert.That(node.HasHostStatus, Is.False, "a new connection has no host yet");

        node.Connection.Hostname = "new.example";

        await HostStatusMonitorTests.WaitFor(() => node.HostStatus == HostStatus.Online);
        Assert.That(_checks, Has.Some.StartsWith("new.example:"));
    }
}
