using mRemoteNG.App;
using mRemoteNG.Avalonia.Platform;
using mRemoteNG.Avalonia.Services;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Config.Settings;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Security;
using mRemoteNG.Tools;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class ImportTests
{
    private TempDirectory _directory = null!;
    private AppPaths _paths = null!;
    private AppSettings _settings = null!;
    private FakeDialogService _dialogs = null!;
    private MainWindowViewModel _viewModel = null!;

    [SetUp]
    public async Task Setup()
    {
        _directory = new TempDirectory();
        _paths = new AppPaths(_directory.File("config"), _directory.File("data"));
        _settings = new AppSettings();
        CoreRuntime.Settings = _settings.Core;
        _dialogs = new FakeDialogService();
        _viewModel = new MainWindowViewModel(_settings, new AppSettingsStore(_paths.SettingsFile), _paths, _dialogs, action => action());
        await _viewModel.InitializeAsync();
    }

    [TearDown]
    public void Teardown()
    {
        _directory.Dispose();
        CoreRuntime.Settings = new CoreSettings();
    }

    [Test]
    public async Task ImportedConnectionsLandInTheSelectedFolderAndAreSaved()
    {
        string other = _directory.File("colleague.xml");
        ConnectionFileService otherFile = new(_settings);
        otherFile.CreateNew(other);
        otherFile.RootNode!.AddChild(new ConnectionInfo { Name = "their-server" });
        otherFile.Save();
        _dialogs.FileToImport = other;

        await _viewModel.ImportCommand.ExecuteAsync(null);

        ContainerInfo imported = (ContainerInfo)_viewModel.SelectedNode!.Connection;
        Assert.That(imported.Name, Is.EqualTo("colleague"));
        Assert.That(imported.Children.Single().Name, Is.EqualTo("their-server"));
        Assert.That(ReopenDefaultFile().GetRecursiveChildList().Select(node => node.Name), Does.Contain("their-server"));
    }

    [Test]
    public async Task AnUnknownFormatIsReportedAndTheFileStillSaves()
    {
        string unknown = _directory.File("sessions.txt");
        File.WriteAllText(unknown, "not importable");
        _dialogs.FileToImport = unknown;

        await _viewModel.ImportCommand.ExecuteAsync(null);
        _viewModel.AddConnectionCommand.Execute(null);

        Assert.That(_dialogs.Errors, Has.Count.EqualTo(1));
        Assert.That(ReopenDefaultFile().GetRecursiveChildList(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AProtectedFileAsksForItsPassword()
    {
        _dialogs.FileToImport = SaveProtectedFile("pw");
        _dialogs.Passwords.Enqueue("wrong");
        _dialogs.Passwords.Enqueue("pw");

        await _viewModel.ImportCommand.ExecuteAsync(null);

        Assert.That(_dialogs.Passwords, Is.Empty);
        Assert.That(_dialogs.Errors, Is.Empty);
        Assert.That(((ContainerInfo)_viewModel.SelectedNode!.Connection).Children.Single().Name, Is.EqualTo("db1"));
    }

    [Test]
    public async Task CancellingThePasswordImportsNothingAndShowsNoError()
    {
        _dialogs.FileToImport = SaveProtectedFile("pw");

        await _viewModel.ImportCommand.ExecuteAsync(null);

        Assert.That(_dialogs.Errors, Is.Empty);
        Assert.That(ReopenDefaultFile().GetRecursiveChildList(), Is.Empty);
    }

    private string SaveProtectedFile(string password)
    {
        string path = _directory.File("protected.xml");
        bool wasLoaded = CoreRuntime.IsConnectionsFileLoaded;
        ConnectionFileService file = new(_settings);
        file.CreateNew(path);
        file.RootNode!.PasswordString = password;
        file.RootNode.AddChild(new ConnectionInfo { Name = "db1" });
        file.Save();
        CoreRuntime.IsConnectionsFileLoaded = wasLoaded;
        return path;
    }

    private Tree.ConnectionTreeModel ReopenDefaultFile()
    {
        bool wasLoaded = CoreRuntime.IsConnectionsFileLoaded;
        try
        {
            return new ConnectionFileService(_settings).Open(_paths.DefaultConnectionFile, () => Optional<System.Security.SecureString>.Empty);
        }
        finally
        {
            CoreRuntime.IsConnectionsFileLoaded = wasLoaded;
        }
    }
}
