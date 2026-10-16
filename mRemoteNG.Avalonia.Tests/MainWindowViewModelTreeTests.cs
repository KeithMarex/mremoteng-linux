using mRemoteNG.App;
using mRemoteNG.Avalonia.Platform;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Config.Settings;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Tree;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

/// <summary>Moving nodes in the tree (drag and drop, move up/down) through the main view model.</summary>
public class MainWindowViewModelTreeTests
{
    private TempDirectory _directory = null!;
    private MainWindowViewModel _viewModel = null!;
    private ConnectionNodeViewModel _root = null!;

    [SetUp]
    public async Task Setup()
    {
        _directory = new TempDirectory();
        AppPaths paths = new(_directory.File("config"), _directory.File("data"));
        AppSettings settings = new();
        CoreRuntime.Settings = settings.Core;
        _viewModel = new MainWindowViewModel(settings, new AppSettingsStore(paths.SettingsFile), paths, new FakeDialogService(), action => action());
        await _viewModel.InitializeAsync();
        _root = _viewModel.RootNodes.Single();
        _viewModel.SelectedNode = _root;
        _viewModel.AddFolderCommand.Execute(null);
        _viewModel.SelectedNode = _root;
        _viewModel.AddConnectionCommand.Execute(null);
        _viewModel.SelectedNode = _root;
        _viewModel.AddConnectionCommand.Execute(null);
    }

    [TearDown]
    public void Teardown()
    {
        _directory.Dispose();
        CoreRuntime.Settings = new CoreSettings();
    }

    [Test]
    public void DroppingAConnectionOnAFolderMovesItThere()
    {
        ConnectionNodeViewModel folder = _root.Children[0];
        ConnectionNodeViewModel connection = _root.Children[1];

        _viewModel.Move(connection, folder, MovePosition.Onto);

        Assert.That(((ContainerInfo)folder.Connection).Children, Has.Count.EqualTo(1));
        Assert.That(_viewModel.SelectedNode?.Connection, Is.SameAs(connection.Connection));
    }

    [Test]
    public void AFolderCannotBeDroppedIntoItself()
    {
        ConnectionNodeViewModel folder = _root.Children[0];

        Assert.That(_viewModel.CanMove(folder, folder, MovePosition.Onto), Is.False);
    }

    [Test]
    public void MoveUpAndDownReorderWithinTheFolder()
    {
        ConnectionInfo last = _root.Children[2].Connection;
        _viewModel.SelectedNode = _root.Children[2];

        _viewModel.MoveUpCommand.Execute(null);
        Assert.That(_root.Children[1].Connection, Is.SameAs(last));

        _viewModel.MoveDownCommand.Execute(null);
        Assert.That(_root.Children[2].Connection, Is.SameAs(last));
        Assert.That(_viewModel.MoveDownCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void SearchFiltersTheTree()
    {
        _root.Children[1].Connection.Name = "unique-name";

        _viewModel.SearchText = "unique";

        Assert.That(_root.Children.Where(node => node.IsVisible).Select(node => node.Name), Is.EqualTo(new[] { "unique-name" }));
    }
}
