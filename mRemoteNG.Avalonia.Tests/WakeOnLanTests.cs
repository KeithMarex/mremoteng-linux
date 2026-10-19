using System.Net;
using System.Net.Sockets;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Platform;
using mRemoteNG.Avalonia.Services;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Config.Settings;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class WakeOnLanTests
{
    private static readonly byte[] Mac = [0x00, 0x11, 0x22, 0xAA, 0xBB, 0xCC];

    [TestCase("00:11:22:AA:BB:CC")]
    [TestCase("00-11-22-aa-bb-cc")]
    [TestCase("0011.22AA.BBCC")]
    [TestCase("00 11 22 AA BB CC")]
    [TestCase(" 001122aabbcc ")]
    public void MacAddressesAreReadInTheUsualNotations(string text) =>
        Assert.That(WakeOnLan.ParseMacAddress(text), Is.EqualTo(Mac));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("00:11:22:AA:BB")]
    [TestCase("00:11:22:AA:BB:CC:DD")]
    [TestCase("GG:11:22:AA:BB:CC")]
    [TestCase("00_11_22_AA_BB_CC")]
    public void AnythingElseIsNoMacAddress(string? text) => Assert.That(WakeOnLan.ParseMacAddress(text), Is.Null);

    [Test]
    public void TheMagicPacketIsSixTimesFFThenTheAddressSixteenTimes()
    {
        byte[] packet = WakeOnLan.MagicPacket(Mac);

        Assert.That(packet, Has.Length.EqualTo(102));
        Assert.That(packet.Take(6), Is.All.EqualTo(0xFF));
        for (int copy = 1; copy <= 16; copy++)
            Assert.That(packet.Skip(copy * 6).Take(6), Is.EqualTo(Mac));
    }

    [TestCase("192.168.1.20", "255.255.255.0", "192.168.1.255")]
    [TestCase("10.1.2.3", "255.255.0.0", "10.1.255.255")]
    [TestCase("172.16.5.9", "255.255.255.252", "172.16.5.11")]
    [TestCase("10.8.0.6", "255.255.255.255", null)]
    [TestCase("10.8.0.6", "255.255.255.254", null)]
    public void EachNetworkHasItsBroadcastAddress(string address, string mask, string? broadcast) =>
        Assert.That(WakeOnLan.BroadcastAddress(IPAddress.Parse(address), IPAddress.Parse(mask))?.ToString(), Is.EqualTo(broadcast));

    [Test]
    public void TheDefaultTargetsIncludeTheLimitedBroadcastOnPort9() =>
        Assert.That(WakeOnLan.BroadcastTargets(), Does.Contain(new IPEndPoint(IPAddress.Broadcast, 9)));

    [Test]
    public async Task ThePacketIsSent()
    {
        using UdpClient listener = Listener();

        await WakeOnLan.SendAsync(Mac, [(IPEndPoint)listener.Client.LocalEndPoint!]);

        Assert.That((await Receive(listener)).Buffer, Is.EqualTo(WakeOnLan.MagicPacket(Mac)));
    }

    internal static UdpClient Listener() => new(new IPEndPoint(IPAddress.Loopback, 0));

    internal static async Task<UdpReceiveResult> Receive(UdpClient listener) =>
        await listener.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
}

/// <summary>"Wake up (Wake-on-LAN)" in the connection tree's menu.</summary>
public class WakeOnLanCommandTests
{
    private TempDirectory _directory = null!;
    private FakeDialogService _dialogs = null!;
    private MainWindowViewModel _viewModel = null!;
    private ConnectionNodeViewModel _folder = null!;
    private UdpClient _listener = null!;

    [SetUp]
    public async Task Setup()
    {
        _directory = new TempDirectory();
        AppPaths paths = new(_directory.File("config"), _directory.File("data"));
        AppSettings settings = new();
        CoreRuntime.Settings = settings.Core;
        CoreRuntime.MessageCollector.ClearMessages();
        _dialogs = new FakeDialogService();
        _viewModel = new MainWindowViewModel(settings, new AppSettingsStore(paths.SettingsFile), paths, _dialogs, action => action());
        await _viewModel.InitializeAsync();
        _listener = WakeOnLanTests.Listener();
        _viewModel.WakeOnLanTargets = () => [(IPEndPoint)_listener.Client.LocalEndPoint!];

        ConnectionNodeViewModel root = _viewModel.RootNodes.Single();
        _viewModel.SelectedNode = root;
        _viewModel.AddFolderCommand.Execute(null);
        _folder = root.Children.Single();
        for (int i = 0; i < 2; i++)
        {
            _viewModel.SelectedNode = _folder;
            _viewModel.AddConnectionCommand.Execute(null);
        }
    }

    [TearDown]
    public void Teardown()
    {
        _listener.Dispose();
        _directory.Dispose();
        CoreRuntime.Settings = new CoreSettings();
    }

    [Test]
    public void OnlyAConnectionWithAMacAddressOrAFolderHoldingOneCanBeWoken()
    {
        _viewModel.SelectedNode = _folder.Children[0];
        Assert.That(_viewModel.WakeOnLanCommand.CanExecute(null), Is.False, "no MAC address");
        _viewModel.SelectedNode = _folder;
        Assert.That(_viewModel.WakeOnLanCommand.CanExecute(null), Is.False, "none in the folder");

        _folder.Children[1].Connection.MacAddress = "00:11:22:aa:bb:cc";

        Assert.That(_viewModel.WakeOnLanCommand.CanExecute(null), Is.True, "the folder");
        _viewModel.SelectedNode = _folder.Children[1];
        Assert.That(_viewModel.WakeOnLanCommand.CanExecute(null), Is.True);
        _viewModel.SelectedNode = _viewModel.RootNodes.Single();
        Assert.That(_viewModel.WakeOnLanCommand.CanExecute(null), Is.False, "not the whole file");
    }

    [Test]
    public async Task WakingAFolderSendsAPacketForEachConnectionWithAMacAddress()
    {
        _folder.Children[1].Connection.MacAddress = "00:11:22:aa:bb:cc";
        _viewModel.SelectedNode = _folder;

        await _viewModel.WakeOnLanCommand.ExecuteAsync(null);

        UdpReceiveResult received = await WakeOnLanTests.Receive(_listener);
        Assert.That(received.Buffer.Skip(6).Take(6), Is.EqualTo(new byte[] { 0x00, 0x11, 0x22, 0xAA, 0xBB, 0xCC }));
        Assert.That(_listener.Available, Is.Zero, "one packet");
        Assert.That(_viewModel.Messages.Selected?.Text, Does.Contain("00:11:22:aa:bb:cc"));
        Assert.That(_dialogs.Errors, Is.Empty);
    }

    [Test]
    public async Task AnInvalidMacAddressIsReported()
    {
        _folder.Children[0].Connection.MacAddress = "00:11:22";
        _viewModel.SelectedNode = _folder.Children[0];

        await _viewModel.WakeOnLanCommand.ExecuteAsync(null);

        Assert.That(_dialogs.Errors.Single(), Does.Contain("00:11:22"));
        Assert.That(_listener.Available, Is.Zero);
    }
}
