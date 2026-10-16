using System.Security;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Platform;
using mRemoteNG.Avalonia.Services;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Config.Settings;
using mRemoteNG.Connection;
using mRemoteNG.Security;
using mRemoteNG.Tools;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class MainWindowViewModelTests
{
    private TempDirectory _directory = null!;
    private AppPaths _paths = null!;
    private AppSettings _settings = null!;
    private FakeDialogService _dialogs = null!;
    private MainWindowViewModel _viewModel = null!;

    [SetUp]
    public void Setup()
    {
        _directory = new TempDirectory();
        _paths = new AppPaths(_directory.File("config"), _directory.File("data"));
        _settings = new AppSettings();
        CoreRuntime.Settings = _settings.Core;
        CoreRuntime.IsConnectionsFileLoaded = false;
        _dialogs = new FakeDialogService();
        _viewModel = new MainWindowViewModel(_settings, new AppSettingsStore(_paths.SettingsFile), _paths, _dialogs, action => action());
    }

    [TearDown]
    public void Teardown()
    {
        _directory.Dispose();
        CoreRuntime.Settings = new CoreSettings();
    }

    [Test]
    public async Task StartupCreatesTheDefaultFileWhenThereIsNone()
    {
        await _viewModel.InitializeAsync();

        Assert.That(File.Exists(_paths.DefaultConnectionFile), Is.True);
        Assert.That(_viewModel.RootNodes, Has.Count.EqualTo(1));
        Assert.That(_viewModel.Title, Is.EqualTo("confCons.xml - mRemoteNG"));
    }

    [Test]
    public async Task StartupOpensTheLastUsedFile()
    {
        string shared = _directory.File("shared/team.xml");
        ConnectionFileService service = new(_settings);
        service.CreateNew(shared);
        service.RootNode!.AddChild(new ConnectionInfo { Name = "jump-host" });
        service.Save();
        _settings.ConnectionFile = shared;

        await _viewModel.InitializeAsync();

        Assert.That(_viewModel.FilePath, Is.EqualTo(shared));
        Assert.That(_viewModel.RootNodes.Single().Children.Single().Name, Is.EqualTo("jump-host"));
    }

    [Test]
    public async Task NewConnectionIsAddedToTheSelectedFolderAndSaved()
    {
        await _viewModel.InitializeAsync();

        _viewModel.AddConnectionCommand.Execute(null);

        Assert.That(_viewModel.SelectedNode?.Connection, Is.InstanceOf<ConnectionInfo>());
        Assert.That(ReopenDefaultFile().GetRecursiveChildList(), Has.Count.EqualTo(1));
        Assert.That(_viewModel.IsDirty, Is.False);
    }

    [Test]
    public async Task DeleteAsksFirst()
    {
        await _viewModel.InitializeAsync();
        _viewModel.AddConnectionCommand.Execute(null);

        _dialogs.ConfirmAnswer = false;
        await _viewModel.DeleteCommand.ExecuteAsync(null);
        Assert.That(ReopenDefaultFile().GetRecursiveChildList(), Has.Count.EqualTo(1));

        _dialogs.ConfirmAnswer = true;
        await _viewModel.DeleteCommand.ExecuteAsync(null);
        Assert.That(ReopenDefaultFile().GetRecursiveChildList(), Is.Empty);
        Assert.That(_dialogs.Confirmations, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ProtectingTheFileUsesTheNewPassword()
    {
        await _viewModel.InitializeAsync();
        _dialogs.NewPassword = "team-secret";

        RootPasswordItem().BooleanValue = true;

        Assert.That(ReopenDefaultFile(() => Password("team-secret")).RootNodes, Has.Count.EqualTo(1));
        Assert.Throws<InvalidDataException>(() => ReopenDefaultFile(() => Optional<SecureString>.Empty));
    }

    [Test]
    public async Task CancellingTheNewPasswordLeavesTheFileUnprotected()
    {
        await _viewModel.InitializeAsync();
        _dialogs.NewPassword = null;

        RootPasswordItem().BooleanValue = true;

        Assert.That(((RootNodeInfo)_viewModel.RootNodes.Single().Connection).Password, Is.False);
        Assert.DoesNotThrow(() => ReopenDefaultFile());
    }

    [Test]
    public async Task OpeningAProtectedFileAsksForItsPassword()
    {
        await _viewModel.InitializeAsync();
        string protectedFile = _directory.File("protected.xml");
        ConnectionFileService service = new(_settings);
        service.CreateNew(protectedFile);
        service.RootNode!.PasswordString = "pw";
        service.Save();
        _dialogs.FileToOpen = protectedFile;
        _dialogs.Passwords.Enqueue("pw");

        await _viewModel.OpenFileCommand.ExecuteAsync(null);

        Assert.That(_viewModel.FilePath, Is.EqualTo(protectedFile));
        Assert.That(_dialogs.Errors, Is.Empty);
    }

    [Test]
    public async Task AFileThatCannotBeOpenedIsReportedAndTheCurrentOneStays()
    {
        await _viewModel.InitializeAsync();
        string broken = _directory.File("broken.xml");
        File.WriteAllText(broken, "this is not a connection file");
        _dialogs.FileToOpen = broken;

        await _viewModel.OpenFileCommand.ExecuteAsync(null);

        Assert.That(_dialogs.Errors, Has.Count.EqualTo(1));
        Assert.That(_viewModel.FilePath, Is.EqualTo(_paths.DefaultConnectionFile));
        _viewModel.AddConnectionCommand.Execute(null);
        Assert.That(ReopenDefaultFile().GetRecursiveChildList(), Has.Count.EqualTo(1), "the current file must still save");
    }

    [Test]
    public async Task InheritanceEditsAreSaved()
    {
        await _viewModel.InitializeAsync();
        _viewModel.AddFolderCommand.Execute(null);
        _viewModel.AddConnectionCommand.Execute(null);
        _viewModel.PropertyPanel.ShowInheritanceCommand.Execute(null);

        PropertyItemViewModel username = _viewModel.PropertyPanel.Groups.SelectMany(group => group.Items)
                                                   .Single(item => item.Name == nameof(ConnectionInfoInheritance.Username));
        username.SelectedChoice = username.Choices.Single(choice => Equals(choice.Value, true));

        ConnectionInfo saved = ReopenDefaultFile().GetRecursiveChildList().Single(node => node.GetTreeNodeType() == Tree.TreeNodeType.Connection);
        Assert.That(saved.Inheritance.Username, Is.True);
        Assert.That(_viewModel.IsDirty, Is.False);
    }

    [Test]
    public async Task TheNewestNotificationIsShownInFull()
    {
        await _viewModel.InitializeAsync();

        CoreRuntime.MessageCollector.AddMessage(Messages.MessageClass.WarningMsg, "first");
        CoreRuntime.MessageCollector.AddMessage(Messages.MessageClass.ErrorMsg, "second");

        Assert.That(_viewModel.Messages.Selected?.Text, Is.EqualTo("second"));
    }

    private PropertyItemViewModel RootPasswordItem()
    {
        _viewModel.SelectedNode = _viewModel.RootNodes.Single();
        return _viewModel.PropertyPanel.Groups.SelectMany(group => group.Items).Single(item => item.Name == nameof(RootNodeInfo.Password));
    }

    private Tree.ConnectionTreeModel ReopenDefaultFile(Func<Optional<SecureString>>? passwordPrompt = null)
    {
        bool wasLoaded = CoreRuntime.IsConnectionsFileLoaded;
        try
        {
            return new ConnectionFileService(_settings).Open(_paths.DefaultConnectionFile, passwordPrompt ?? (() => Optional<SecureString>.Empty));
        }
        finally
        {
            CoreRuntime.IsConnectionsFileLoaded = wasLoaded;
        }
    }

    private static Optional<SecureString> Password(string password) => new(password.ConvertToSecureString());
}
