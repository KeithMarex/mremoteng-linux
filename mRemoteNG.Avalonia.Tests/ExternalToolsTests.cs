using mRemoteNG.App;
using mRemoteNG.Avalonia.Platform;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Config.Settings;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Container;
using mRemoteNG.ExternalTools;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class ExternalToolLauncherTests
{
    private readonly ExternalToolLauncher _launcher = new(new SessionPrograms { Find = program => program.StartsWith('/') ? program : "/usr/bin/" + program });
    private readonly ConnectionInfo _connection = new() { Name = "Web 1", Hostname = "web1", Password = "s3cret" };

    [Test]
    public void TheToolsCommandIsFilledInForTheConnection()
    {
        ExternalToolDefinition tool = new() { DisplayName = "Ping", FileName = "ping", Arguments = "-c 3 %HOSTNAME%" };

        SessionPlan plan = _launcher.Plan(tool, _connection);

        Assert.That((plan.Program, plan.Arguments), Is.EqualTo(("/usr/bin/ping", (IReadOnlyList<string>)["-c", "3", "web1"])));
        Assert.That((plan.WorkingDirectory, plan.Warnings.Count), Is.EqualTo(((string?)null, 0)));
    }

    [Test]
    public void ThePasswordIsNeverShown()
    {
        ExternalToolDefinition tool = new() { FileName = "sshpass", Arguments = "-p %PASSWORD% ssh %HOSTNAME%" };

        Assert.That(_launcher.Plan(tool, _connection).ToString(), Is.EqualTo("sshpass -p *** ssh web1"));
    }

    [Test]
    public void RunningElevatedAndAMissingWorkingDirectoryAreReported()
    {
        ExternalToolDefinition tool = new() { DisplayName = "Admin", FileName = "ls", RunElevated = true, WorkingDir = "/does/not/exist" };

        SessionPlan plan = _launcher.Plan(tool, _connection);

        Assert.That(plan.Warnings, Has.Count.EqualTo(2));
        Assert.That(plan.WorkingDirectory, Is.Null);
    }

    [Test]
    public void AToolWithoutProgramOrWithAMissingOneIsRefused()
    {
        ExternalToolLauncher launcher = new(new SessionPrograms { Find = _ => null });

        Assert.Throws<SessionException>(() => _launcher.Plan(new ExternalToolDefinition { DisplayName = "Empty" }, _connection));
        Assert.That(Assert.Throws<SessionException>(() => launcher.Plan(new ExternalToolDefinition { FileName = "nmap" }, _connection))!.Message,
                    Does.Contain("nmap"));
    }
}

public class ExternalToolsTests
{
    private TempDirectory _directory = null!;
    private FakeDialogService _dialogs = null!;
    private MainWindowViewModel _viewModel = null!;
    private readonly List<SessionPlan> _started = [];
    private TaskCompletionSource<int> _exit = null!;

    [SetUp]
    public async Task Setup()
    {
        _started.Clear();
        _exit = new TaskCompletionSource<int>();
        _exit.SetResult(0);
        CoreRuntime.MessageCollector.ClearMessages();
        _directory = new TempDirectory();
        AppPaths paths = new(_directory.File("config"), _directory.File("data"));
        ExternalToolsFile.Save(paths.ExternalToolsFile,
        [
            new ExternalToolDefinition { DisplayName = "Ping", FileName = "ping", Arguments = "-c 3 %HOSTNAME%" },
            new ExternalToolDefinition { DisplayName = "Mtr", FileName = "mtr", Arguments = "%HOSTNAME%", TryIntegrate = true },
            new ExternalToolDefinition { DisplayName = "Wake", FileName = "wakeonlan", Arguments = "%MACADDRESS%", WaitForExit = true }
        ]);
        AppSettings settings = new();
        CoreRuntime.Settings = settings.Core;
        _dialogs = new FakeDialogService();
        SessionPrograms programs = new()
        {
            Find = program => "/usr/bin/" + program,
            StartDetached = plan =>
            {
                _started.Add(plan);
                return _exit.Task;
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
    public void TheToolsAreLoadedAndOfferedInTheProperties()
    {
        Assert.That(_viewModel.ExternalTools.Select(tool => tool.DisplayName), Is.EqualTo(new[] { "Ping", "Mtr", "Wake" }));

        ConnectionInfo connection = AddConnection(ProtocolType.IntApp);
        PropertyItemViewModel extApp = _viewModel.PropertyPanel.Groups.SelectMany(group => group.Items)
                                                 .Single(item => item.Name == nameof(ConnectionInfo.ExtApp));

        Assert.That(extApp.IsChoice, Is.True);
        Assert.That(extApp.Choices.Select(choice => choice.Value), Is.EqualTo(new[] { "", "Ping", "Mtr", "Wake" }));
        extApp.SelectedChoice = extApp.Choices[1];
        Assert.That(connection.ExtApp, Is.EqualTo("Ping"));
    }

    [Test]
    public async Task AnExternalToolConnectionRunsItsToolOnItsOwn()
    {
        ConnectionInfo connection = AddConnection(ProtocolType.IntApp, extApp: "ping");

        await _viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.That(_started.Single().Arguments, Is.EqualTo(new[] { "-c", "3", "host1" }));
        Assert.That(_viewModel.Sessions.Items, Is.Empty);
        Assert.That(connection.OpenConnections, Is.Empty);
    }

    [Test]
    public async Task AnIntegratedToolRunsInATerminalTab()
    {
        AddConnection(ProtocolType.IntApp, extApp: "Mtr");

        await _viewModel.ConnectCommand.ExecuteAsync(null);
        SessionViewModel tab = (SessionViewModel)_viewModel.Sessions.Items.Single();
        FakeTerminalHost terminal = new();
        await tab.AttachAsync(terminal);

        Assert.That((terminal.Started.Single().Plan.Program, terminal.Started.Single().Plan.Arguments),
                    Is.EqualTo(("/usr/bin/mtr", (IReadOnlyList<string>)["host1"])));
        Assert.That(_started, Is.Empty);

        // Unlike a terminal session, the tab stays when the tool is done, so its output can be read.
        terminal.Exit(0);
        Assert.That(_viewModel.Sessions.Items.Single().HasEnded, Is.True);
    }

    [Test]
    public async Task AnUnknownToolIsReported()
    {
        AddConnection(ProtocolType.IntApp, extApp: "Nmap");

        await _viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.That(_dialogs.Errors.Single(), Does.Contain("Nmap"));
        Assert.That(_started, Is.Empty);
    }

    [Test]
    public async Task ThePreConnectionToolRunsFirstAndIsWaitedFor()
    {
        _exit = new TaskCompletionSource<int>();
        AddConnection(ProtocolType.SSH2, preExtApp: "Wake");

        Task connecting = _viewModel.ConnectCommand.ExecuteAsync(null);

        Assert.That(_started.Single().Arguments, Is.EqualTo(new[] { "00:11:22:33:44:55" }));
        Assert.That(_viewModel.Sessions.Items, Is.Empty, "the session waits for the tool");
        _exit.SetResult(0);
        await connecting;
        Assert.That(_viewModel.Sessions.Items, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ThePostConnectionToolRunsWhenTheSessionEnds()
    {
        AddConnection(ProtocolType.SSH2, postExtApp: "Ping");
        await _viewModel.ConnectCommand.ExecuteAsync(null);
        FakeTerminalHost terminal = new();
        await ((SessionViewModel)_viewModel.Sessions.Items.Single()).AttachAsync(terminal);
        Assert.That(_started, Is.Empty);

        terminal.Exit(1);

        Assert.That(_started.Single().Arguments, Is.EqualTo(new[] { "-c", "3", "host1" }));
    }

    [Test]
    public async Task AToolRunOnAFolderRunsForEachConnectionInIt()
    {
        _viewModel.AddFolderCommand.Execute(null);
        ContainerInfo folder = (ContainerInfo)_viewModel.SelectedNode!.Connection;
        folder.AddChild(new ConnectionInfo { Name = "a", Hostname = "a.example" });
        folder.AddChild(new ConnectionInfo { Name = "b", Hostname = "b.example" });

        await _viewModel.RunExternalToolCommand.ExecuteAsync(_viewModel.ExternalTools[0]);

        Assert.That(_started.Select(plan => plan.Arguments[^1]), Is.EqualTo(new[] { "a.example", "b.example" }));
    }

    [Test]
    public async Task EditedToolsAreSavedAndCancelledEditsAreNot()
    {
        _dialogs.EditExternalTools = editor =>
        {
            editor.Selected!.DisplayName = "Renamed";
            return false;
        };
        await _viewModel.OpenExternalToolsCommand.ExecuteAsync(null);
        Assert.That(_viewModel.ExternalTools[0].DisplayName, Is.EqualTo("Ping"));

        _dialogs.EditExternalTools = editor =>
        {
            editor.DeleteCommand.Execute(null);
            editor.NewCommand.Execute(null);
            editor.Selected!.FileName = "nmap";
            return true;
        };
        await _viewModel.OpenExternalToolsCommand.ExecuteAsync(null);

        string[] saved = ExternalToolsFile.Load(new AppPaths(_directory.File("config"), _directory.File("data")).ExternalToolsFile)
                                          .Select(tool => tool.FileName).ToArray();
        Assert.That(saved, Is.EqualTo(new[] { "mtr", "wakeonlan", "nmap" }));
        Assert.That(_viewModel.ExternalTools.Select(tool => tool.FileName), Is.EqualTo(saved));
    }

    private ConnectionInfo AddConnection(ProtocolType protocol, string extApp = "", string preExtApp = "", string postExtApp = "")
    {
        _viewModel.AddConnectionCommand.Execute(null);
        ConnectionInfo connection = _viewModel.SelectedNode!.Connection;
        connection.Protocol = protocol;
        connection.Hostname = "host1";
        connection.MacAddress = "00:11:22:33:44:55";
        connection.ExtApp = extApp;
        connection.PreExtApp = preExtApp;
        connection.PostExtApp = postExtApp;
        // Show the properties of the protocol just set.
        _viewModel.PropertyPanel.Connection = null;
        _viewModel.PropertyPanel.Connection = connection;
        return connection;
    }
}
