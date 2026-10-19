using mRemoteNG.App;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Avalonia.Sessions.Rdp;
using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class RdpSessionViewModelTests
{
    private ConnectionInfo _connection = null!;
    private FakeRdpView _view = null!;
    private FakeRdpProcess _process = null!;
    private Func<RdpDisplay, Task<RdpSession>> _connect = null!;
    private RdpSessionViewModel _session = null!;
    private bool _closeRequested;

    [SetUp]
    public void Setup()
    {
        CoreRuntime.MessageCollector.ClearMessages();
        _connection = new ConnectionInfo { Name = "Server 1", Protocol = ProtocolType.RDP, Hostname = "srv1" };
        _view = new FakeRdpView();
        _process = new FakeRdpProcess();
        _connect = _ => Task.FromResult(new RdpSession(new FreeRdpPlan("/usr/bin/xfreerdp3", ["/v:srv1", "/p:secret"]), _process));
        _session = new RdpSessionViewModel(_connection, (_, display, _) => _connect(display), action => action());
        _closeRequested = false;
        _session.CloseRequested += (_, _) => _closeRequested = true;
    }

    [Test]
    public async Task TheSessionStartsOnceTheTabHasAWindow()
    {
        await _session.AttachAsync(_view);

        Assert.That(_session.State, Is.EqualTo(SessionState.Running));
        Assert.That(_session.IsLive, Is.True);
        Assert.That(_session.ShowsStatus, Is.False);
        Assert.That(_connection.OpenConnections, Does.Contain(_session));
        Assert.That(CoreRuntime.MessageCollector.Messages.Select(m => m.Text), Has.Some.Contains("/p:***").And.None.Contains("secret"));
    }

    [Test]
    public async Task ALogoffClosesTheTab()
    {
        await _session.AttachAsync(_view);

        _process.Exit(2);

        Assert.That(_closeRequested, Is.True);
        Assert.That(_connection.OpenConnections, Is.Empty);
        Assert.That(_process.Disposed, Is.True);
    }

    [Test]
    public async Task AFailureKeepsTheTabWithTheReasonAndReconnect()
    {
        await _session.AttachAsync(_view);

        _process.Exit(132, "ERRCONNECT_AUTHENTICATION_FAILED");

        Assert.That(_closeRequested, Is.False);
        Assert.That(_session.State, Is.EqualTo(SessionState.Failed));
        Assert.That(_session.StatusText, Does.Contain("logon failed"));
        Assert.That(_session.ReconnectCommand.CanExecute(null), Is.True);
        Assert.That(CoreRuntime.MessageCollector.Messages.Select(m => m.Text), Has.Some.Contains("ERRCONNECT_AUTHENTICATION_FAILED"));
    }

    [Test]
    public async Task AnUnknownExitCodeShowsXfreerdpsLastError()
    {
        await _session.AttachAsync(_view);

        _process.Exit(250, "transport_read_layer: something broke");

        Assert.That(_session.StatusText, Is.EqualTo("transport_read_layer: something broke"));
    }

    [Test]
    public async Task DisconnectingStopsXfreerdpAndKeepsTheTab()
    {
        await _session.AttachAsync(_view);

        _session.Disconnect();
        _process.Exit(0);

        Assert.That(_process.Stopped, Is.True);
        Assert.That(_closeRequested, Is.False);
        Assert.That(_session.StatusText, Is.EqualTo(UiText.Disconnected));
        Assert.That(_session.HasEnded, Is.True);
    }

    [Test]
    public async Task ReconnectStartsANewSession()
    {
        await _session.AttachAsync(_view);
        _process.Exit(1);
        FakeRdpProcess second = new();
        _process = second;

        await _session.ReconnectCommand.ExecuteAsync(null);

        Assert.That(_session.State, Is.EqualTo(SessionState.Running));
        second.Exit(2);
        Assert.That(_closeRequested, Is.True);
    }

    [Test]
    public async Task AConnectionThatCannotStartIsReported()
    {
        _connect = _ => throw new SessionException(UiText.FreeRdpNotFound);

        await _session.AttachAsync(_view);

        Assert.That(_session.State, Is.EqualTo(SessionState.Failed));
        Assert.That(_session.StatusText, Does.Contain("freerdp3-x11"));
        Assert.That(_connection.OpenConnections, Is.Empty);
    }

    [Test]
    public async Task ClosingTheTabWhileConnectingEndsTheNewSession()
    {
        TaskCompletionSource<RdpSession> connecting = new();
        _connect = _ => connecting.Task;
        Task attach = _session.AttachAsync(_view);
        Assert.That(_session.IsConnecting, Is.True);

        _session.Dispose();
        connecting.SetResult(new RdpSession(new FreeRdpPlan("/usr/bin/xfreerdp3", []), _process));
        await attach;

        Assert.That(_process.Disposed, Is.True);
        Assert.That(_connection.OpenConnections, Is.Empty);
    }

    [Test]
    public async Task ClosingTheTabEndsTheSession()
    {
        await _session.AttachAsync(_view);

        _session.Dispose();

        Assert.That((_process.Stopped, _process.Disposed), Is.EqualTo((true, true)));
        Assert.That(_connection.OpenConnections, Is.Empty);
    }

    [Test]
    public async Task AFullScreenSessionSaysWhereItIs()
    {
        _connect = _ => Task.FromResult(new RdpSession(new FreeRdpPlan("/usr/bin/xfreerdp3", ["/f"]) { OwnWindow = true }, _process));

        await _session.AttachAsync(_view);

        Assert.That(_session.ShowsStatus, Is.True);
        Assert.That(_session.StatusText, Is.EqualTo(UiText.RdpInOwnWindow));
    }

    [Test]
    public async Task ShowingTheTabGivesTheSessionTheKeyboard()
    {
        await _session.AttachAsync(_view);

        _session.Focus();

        Assert.That(_view.Focused, Is.EqualTo(1));
    }

    private sealed class FakeRdpView : IRdpView
    {
        public int Focused { get; private set; }

        public RdpDisplay Describe() => new(4194322, 1000, 700, 1920, 1080);

        public void FocusSession() => Focused++;
    }
}
