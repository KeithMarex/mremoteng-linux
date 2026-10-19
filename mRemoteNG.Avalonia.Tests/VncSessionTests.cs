using Avalonia.Input;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Services;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Avalonia.Views;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Vnc;
using mRemoteNG.Vnc.Tests;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class VncSessionTests
{
    private static readonly byte[] Challenge = Convert.FromHexString("0123456789abcdeffedcba9876543210");

    private RfbTestServer _server = null!;
    private FakeDialogService _dialogs = null!;
    private ConnectionInfo _connection = null!;

    [SetUp]
    public void Setup()
    {
        CoreRuntime.MessageCollector.ClearMessages();
        _server = new RfbTestServer();
        _dialogs = new FakeDialogService();
        _connection = new ConnectionInfo { Name = "Desktop", Protocol = ProtocolType.VNC, Hostname = "127.0.0.1", Port = _server.Port };
    }

    [TearDown]
    public void Teardown() => _server.Dispose();

    [Test]
    public async Task TheTabConnectsWhenShownAndListsItselfAsOpen()
    {
        VncSessionViewModel session = Session();

        Task attach = session.AttachAsync();
        await _server.OfferAsync(1);
        _server.Accept(800, 600, "office");
        await attach;

        Assert.That(session.State, Is.EqualTo(SessionState.Running));
        Assert.That(session.Client!.DesktopName, Is.EqualTo("office"));
        Assert.That(session.StatusText, Does.Contain("office"));
        Assert.That(_connection.OpenConnections, Does.Contain(session));
        session.Dispose();
    }

    [Test]
    public async Task AMissingVncPasswordIsAskedFor()
    {
        _dialogs.Secrets.Enqueue("pw");
        VncSessionViewModel session = Session();

        Task attach = session.AttachAsync();
        await _server.OfferAsync(2);
        _server.Write(Challenge);
        Assert.That(Convert.ToHexString(_server.Read(16)), Is.EqualTo("d9eecfd4b6efab137bafbf5ec61d5f04").IgnoreCase);
        _server.Accept(640, 480);
        await attach;

        Assert.That(_dialogs.SecretPrompts.Single(), Does.Contain("127.0.0.1"));
        Assert.That(session.State, Is.EqualTo(SessionState.Running));
        session.Dispose();
    }

    [Test]
    public async Task AppleRemoteDesktopAsksForTheUserAndPassword()
    {
        _connection.Protocol = ProtocolType.ARD;
        _connection.Username = "koen";
        VncSessionViewModel session = Session();

        Task attach = session.AttachAsync();
        Assert.That(await _server.OfferAsync(2, 30), Is.EqualTo(30));
        await attach;

        // Cancelled: the login was asked for with the stored user name, and nothing was sent.
        Assert.That(_dialogs.LoginQuestions.Single().Initial.Username, Is.EqualTo("koen"));
        Assert.That(session.State, Is.EqualTo(SessionState.Failed));
        Assert.That(session.StatusText, Does.Contain(UiText.ConnectionCancelled));
    }

    [Test]
    public async Task ARefusedLoginIsShownWithReconnect()
    {
        _connection.Password = "wrong";
        VncSessionViewModel session = Session();

        Task attach = session.AttachAsync();
        await _server.OfferAsync(2);
        _server.Write(Challenge);
        _server.Read(16);
        _server.WriteUInt32(1);
        _server.WriteString("Authentication failed");
        await attach;

        Assert.That(session.State, Is.EqualTo(SessionState.Failed));
        Assert.That(session.StatusText, Does.Contain("Authentication failed"));
        Assert.That(session.ReconnectCommand.CanExecute(null), Is.True);
    }

    [Test]
    public async Task AServerThatGoesAwayLeavesTheTabWithTheReason()
    {
        VncSessionViewModel session = await ConnectedAsync();

        _server.Dispose();
        await WaitUntilAsync(() => session.State != SessionState.Running);

        Assert.That(session.State, Is.EqualTo(SessionState.Failed));
        Assert.That(session.StatusText, Does.StartWith("The connection was lost"));
        Assert.That(_connection.OpenConnections, Is.Empty);
    }

    [Test]
    public async Task DisconnectingKeepsTheTab()
    {
        VncSessionViewModel session = await ConnectedAsync();

        session.Disconnect();
        await WaitUntilAsync(() => session.State != SessionState.Running);

        Assert.That((session.State, session.StatusText), Is.EqualTo((SessionState.Ended, UiText.Disconnected)));
    }

    [Test]
    public async Task CtrlAltDelPressesAndReleasesTheThreeKeys()
    {
        VncSessionViewModel session = await ConnectedAsync();

        session.SendCtrlAltDelCommand.Execute(null);

        byte[] keys = _server.Read(6 * 8);
        Assert.That(Enumerable.Range(0, 6).Select(i => (keys[i * 8 + 1], keys[i * 8 + 6], keys[i * 8 + 7])), Is.EqualTo(new[]
        {
            (1, 0xFF, 0xE3), (1, 0xFF, 0xE9), (1, 0xFF, 0xFF), (0, 0xFF, 0xFF), (0, 0xFF, 0xE9), (0, 0xFF, 0xE3)
        }));
        session.Dispose();
    }

    [Test]
    public async Task AViewOnlySessionSendsNoKeys()
    {
        _connection.VNCViewOnly = true;
        VncSessionViewModel session = await ConnectedAsync();

        Assert.That(session.SendCtrlAltDelCommand.CanExecute(null), Is.False);
        session.Dispose();
    }

    [TestCase(Key.Enter, 0xFF0Du)]
    [TestCase(Key.F5, 0xFFC2u)]
    [TestCase(Key.LeftCtrl, 0xFFE3u)]
    [TestCase(Key.RightAlt, 0xFE03u)]
    [TestCase(Key.A, null)]
    [TestCase(Key.D4, null)]
    public void KeysThatTypeNothingHaveTheirOwnKeysym(Key key, uint? expected)
    {
        Assert.That(VncKeyMap.SpecialKeySym(key), Is.EqualTo(expected));
    }

    [Test]
    public void ShortcutsAndTypedCharactersMapToKeysyms()
    {
        Assert.That(VncKeyMap.ShortcutKeySym(Key.C, "\u0003"), Is.EqualTo('c'));
        Assert.That(VncKeyMap.ShortcutKeySym(Key.C, "c"), Is.EqualTo('c'));
        Assert.That(VncKeyMap.FromCodePoint('$'), Is.EqualTo(0x24));
        Assert.That(VncKeyMap.FromCodePoint('é'), Is.EqualTo(0xE9));
        Assert.That(VncKeyMap.FromCodePoint('€'), Is.EqualTo(0x010020AC));
    }

    private VncSessionViewModel Session() =>
        new(_connection, new VncConnector(_dialogs).ConnectAsync, action => action());

    private async Task<VncSessionViewModel> ConnectedAsync()
    {
        VncSessionViewModel session = Session();
        Task attach = session.AttachAsync();
        await _server.OfferAsync(1);
        _server.Accept(64, 32);
        await attach;
        return session;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
            await Task.Delay(25);
    }
}
