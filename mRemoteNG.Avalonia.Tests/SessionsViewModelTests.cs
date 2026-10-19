using mRemoteNG.App;
using mRemoteNG.Avalonia.Platform;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Avalonia.Sessions.Askpass;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Config.Settings;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class SessionsViewModelTests
{
    private TempDirectory _directory = null!;
    private SessionLauncher _launcher = null!;
    private SessionsViewModel _sessions = null!;

    [SetUp]
    public void Setup()
    {
        _directory = new TempDirectory();
        SessionPrograms programs = new() { App = "/usr/lib/mremoteng/mRemoteNG.Avalonia", Find = program => "/usr/bin/" + program };
        _launcher = new SessionLauncher(programs, _directory.File("run"));
        _sessions = new SessionsViewModel(_launcher, _ => throw new AssertionException("no file transfer expected"));
    }

    [TearDown]
    public void Teardown()
    {
        _sessions.CloseAll();
        _directory.Dispose();
    }

    [Test]
    public async Task ASessionStartsWhenItsTerminalIsShownAndCountsAsOpenWhileRunning()
    {
        ConnectionInfo connection = Ssh();
        SessionViewModel session = _sessions.Open(connection);
        FakeTerminalHost terminal = new();

        await session.AttachAsync(terminal);

        Assert.That(terminal.Started.Single().Plan.Arguments, Is.EqualTo(new[] { "-l", "admin", "--", "db1" }));
        Assert.That(session.IsLive, Is.True);
        Assert.That(_sessions.Selected, Is.SameAs(session));
        Assert.That(session.IsSelected, Is.True);
        Assert.That(connection.OpenConnections.Count, Is.EqualTo(1));
        Assert.That(new ConnectionNodeViewModel(connection).IsConnected, Is.True);
    }

    [Test]
    public async Task AStoredPasswordReachesSshOnlyThroughTheAskpassHelper()
    {
        ConnectionInfo connection = Ssh();
        connection.Password = "s3cret";
        FakeTerminalHost terminal = new();

        await _sessions.Open(connection).AttachAsync(terminal);

        (SessionPlan plan, IReadOnlyDictionary<string, string> environment) = terminal.Started.Single();
        Assert.That(environment["SSH_ASKPASS"], Is.EqualTo("/usr/lib/mremoteng/mRemoteNG.Avalonia"));
        Assert.That(environment.Values.Concat(plan.Arguments), Has.None.Contains("s3cret"));
        string socket = environment[AskpassServer.SocketVariable];
        AskpassReply? reply = AskpassClient.AskServer(name => environment.GetValueOrDefault(name), "admin@db1's password: ");
        Assert.That(reply?.Password, Is.EqualTo("s3cret"));

        terminal.Exit(255);
        Assert.That(File.Exists(socket), Is.False, "the askpass socket lives only as long as the session");
    }

    [Test]
    public async Task ACleanExitClosesTheTab()
    {
        ConnectionInfo connection = Ssh();
        FakeTerminalHost terminal = new();
        await _sessions.Open(connection).AttachAsync(terminal);

        terminal.Exit(0);

        Assert.That(_sessions.Items, Is.Empty);
        Assert.That(_sessions.HasSessions, Is.False);
        Assert.That(connection.OpenConnections.Count, Is.Zero);
    }

    [Test]
    public async Task AFailedSessionStaysWithItsReasonAndCanReconnect()
    {
        ConnectionInfo connection = Ssh();
        SessionViewModel session = _sessions.Open(connection);
        FakeTerminalHost terminal = new();
        await session.AttachAsync(terminal);

        terminal.Exit(255);

        Assert.That(session.HasEnded, Is.True);
        Assert.That(session.StatusText, Is.EqualTo(UiText.SessionEnded(255)));
        Assert.That(connection.OpenConnections.Count, Is.Zero);
        Assert.That(session.ReconnectCommand.CanExecute(null), Is.True);

        await session.ReconnectCommand.ExecuteAsync(null);

        Assert.That(terminal.Started, Has.Count.EqualTo(2));
        Assert.That(session.IsLive, Is.True);
    }

    [Test]
    public async Task ADisconnectedSessionKeepsItsTabEvenWhenTheProgramExitsCleanly()
    {
        SessionViewModel session = _sessions.Open(Ssh());
        FakeTerminalHost terminal = new();
        await session.AttachAsync(terminal);

        session.Disconnect();
        terminal.Exit(0);

        Assert.That(terminal.Killed, Is.True);
        Assert.That(_sessions.Items, Is.EqualTo(new[] { session }));
        Assert.That((session.State, session.StatusText), Is.EqualTo((SessionState.Ended, UiText.Disconnected)));

        await session.ReconnectCommand.ExecuteAsync(null);
        terminal.Exit(0);

        Assert.That(_sessions.Items, Is.Empty, "after reconnecting, a clean exit closes the tab again");
    }

    [Test]
    public async Task AConnectionThatCannotBeOpenedShowsWhyInItsTab()
    {
        SessionViewModel session = _sessions.Open(Ssh(hostname: ""));
        FakeTerminalHost terminal = new();

        await session.AttachAsync(terminal);

        Assert.That(terminal.Started, Is.Empty);
        Assert.That(session.State, Is.EqualTo(SessionState.Failed));
        Assert.That(session.StatusText, Does.Contain(UiText.CouldNotConnect(string.Empty).Trim()));
        Assert.That(CoreRuntime.MessageCollector.Messages.Last().Text, Does.Contain(session.StatusText));
    }

    [Test]
    public async Task ClosingATabEndsItsSession()
    {
        ConnectionInfo connection = Ssh();
        FakeTerminalHost terminal = new();
        SessionViewModel first = _sessions.Open(connection);
        await first.AttachAsync(terminal);
        SessionViewModel second = _sessions.Open(Ssh(hostname: "db2"));

        first.CloseCommand.Execute(null);

        Assert.That(terminal.Killed, Is.True);
        Assert.That(_sessions.Items, Is.EqualTo(new[] { second }));
        Assert.That(_sessions.Selected, Is.SameAs(second));
        Assert.That(connection.OpenConnections.Count, Is.Zero);
    }

    private static ConnectionInfo Ssh(string hostname = "db1") =>
        new() { Name = hostname, Protocol = ProtocolType.SSH2, Hostname = hostname, Username = "admin", Port = 22 };
}

public class MainWindowSessionTests
{
    private TempDirectory _directory = null!;
    private FakeDialogService _dialogs = null!;
    private MainWindowViewModel _viewModel = null!;
    private readonly List<SessionPlan> _launched = [];
    private int _launchExitCode;

    [SetUp]
    public async Task Setup()
    {
        _launched.Clear();
        _launchExitCode = 0;
        CoreRuntime.MessageCollector.ClearMessages();
        _directory = new TempDirectory();
        AppPaths paths = new(_directory.File("config"), _directory.File("data"));
        AppSettings settings = new();
        CoreRuntime.Settings = settings.Core;
        _dialogs = new FakeDialogService();
        SessionPrograms programs = new()
        {
            Find = program => "/usr/bin/" + program,
            StartDetached = plan =>
            {
                _launched.Add(plan);
                return Task.FromResult(_launchExitCode);
            }
        };
        SessionLauncher launcher = new(programs, _directory.File("run"));
        _viewModel = new MainWindowViewModel(settings, new AppSettingsStore(paths.SettingsFile), paths, _dialogs, action => action(), launcher);
        await _viewModel.InitializeAsync();
    }

    [TearDown]
    public void Teardown()
    {
        _directory.Dispose();
        CoreRuntime.Settings = new CoreSettings();
    }

    [Test]
    public async Task ConnectOpensTheSelectedConnectionInATab()
    {
        _viewModel.AddConnectionCommand.Execute(null);
        _viewModel.SelectedNode!.Connection.Protocol = ProtocolType.SSH2;

        await _viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.That(_viewModel.Sessions.Items.Single().Connection, Is.SameAs(_viewModel.SelectedNode.Connection));
    }

    [Test]
    public async Task AProtocolThisAppCannotOpenIsReportedWithoutATab()
    {
        _viewModel.AddConnectionCommand.Execute(null);
        _viewModel.SelectedNode!.Connection.Protocol = ProtocolType.WSL;

        await _viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.That(_viewModel.Sessions.Items, Is.Empty);
        Assert.That(_dialogs.Errors.Single(), Does.EndWith(UiText.WslWindowsOnly));
    }

    [Test]
    public async Task AnRdpConnectionOpensARemoteDesktopTab()
    {
        _viewModel.AddConnectionCommand.Execute(null);
        _viewModel.SelectedNode!.Connection.Protocol = ProtocolType.RDP;

        await _viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.That(_viewModel.Sessions.Items.Single(), Is.TypeOf<RdpSessionViewModel>());
        Assert.That(_viewModel.Sessions.Selected, Is.SameAs(_viewModel.Sessions.Items.Single()));
    }

    [TestCase(ProtocolType.VNC)]
    [TestCase(ProtocolType.ARD)]
    public async Task VncAndAppleRemoteDesktopOpenAVncTab(ProtocolType protocol)
    {
        _viewModel.AddConnectionCommand.Execute(null);
        _viewModel.SelectedNode!.Connection.Protocol = protocol;

        await _viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.That(_viewModel.Sessions.Items.Single(), Is.TypeOf<VncSessionViewModel>());
    }

    [Test]
    public async Task AWebConnectionOpensInTheBrowserWithoutATab()
    {
        _viewModel.AddConnectionCommand.Execute(null);
        ConnectionInfo connection = _viewModel.SelectedNode!.Connection;
        connection.Protocol = ProtocolType.HTTPS;
        connection.Hostname = "intranet";
        connection.SetDefaultPort();

        await _viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.That(_viewModel.Sessions.Items, Is.Empty);
        Assert.That(_launched.Single().Arguments, Is.EqualTo(new[] { "https://intranet/" }));
        Assert.That(CoreRuntime.MessageCollector.Messages.Select(m => m.Text), Has.Some.Contains("https://intranet/"));
        Assert.That(_dialogs.Errors, Is.Empty);
    }

    [Test]
    public async Task AProgramThatFailsIsReportedInTheNotifications()
    {
        _viewModel.AddConnectionCommand.Execute(null);
        ConnectionInfo connection = _viewModel.SelectedNode!.Connection;
        connection.Protocol = ProtocolType.AnyDesk;
        connection.Hostname = "123456789";
        _launchExitCode = 3;

        await _viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.That(CoreRuntime.MessageCollector.Messages.Select(m => m.Text), Has.Some.Contains("exit code 3"));
    }

    [Test]
    public async Task AnInvalidWebAddressIsReportedWithoutStartingTheBrowser()
    {
        _viewModel.AddConnectionCommand.Execute(null);
        ConnectionInfo connection = _viewModel.SelectedNode!.Connection;
        connection.Protocol = ProtocolType.HTTP;
        connection.Hostname = "file:///etc/passwd";

        await _viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.That(_launched, Is.Empty);
        Assert.That(_dialogs.Errors.Single(), Does.EndWith(UiText.InvalidWebAddress));
    }

    [Test]
    public void FoldersCannotBeConnected()
    {
        _viewModel.AddFolderCommand.Execute(null);

        Assert.That(_viewModel.ConnectCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task ClosingTheAppWithOpenSessionsAsksFirstAndThenEndsThem()
    {
        _viewModel.AddConnectionCommand.Execute(null);
        ConnectionInfo connection = _viewModel.SelectedNode!.Connection;
        connection.Protocol = ProtocolType.SSH2;
        connection.Hostname = "db1";
        await _viewModel.ConnectCommand.ExecuteAsync(null);
        FakeTerminalHost terminal = new();
        await ((SessionViewModel)_viewModel.Sessions.Items.Single()).AttachAsync(terminal);

        _dialogs.ConfirmAnswer = false;
        Assert.That(await _viewModel.CanCloseAsync(), Is.False);
        Assert.That(terminal.Killed, Is.False);

        _dialogs.ConfirmAnswer = true;
        Assert.That(await _viewModel.CanCloseAsync(), Is.True);
        Assert.That(terminal.Killed, Is.True);
        Assert.That(_dialogs.Confirmations, Has.All.EqualTo(UiText.ConfirmCloseSessions));
    }
}

/// <summary>Records what a session starts and lets a test end it.</summary>
internal sealed class FakeTerminalHost : ITerminalHost
{
    public List<(SessionPlan Plan, IReadOnlyDictionary<string, string> Environment)> Started { get; } = [];

    public bool Killed { get; private set; }

    public event EventHandler<int?>? Exited;

    public Task StartAsync(SessionPlan plan, IReadOnlyDictionary<string, string> environment)
    {
        Started.Add((plan, environment));
        return Task.CompletedTask;
    }

    public void Kill() => Killed = true;

    public void Exit(int? exitCode) => Exited?.Invoke(this, exitCode);
}
