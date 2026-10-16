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

/// <summary>Moving nodes in the tree (drag and drop, move up/down) and the Connections toolbar, through the main view model.</summary>
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

    [Test]
    public void CollapseAllClosesTheFoldersButNotTheRoot()
    {
        ConnectionNodeViewModel folder = _root.Children[0];
        folder.IsExpanded = true;

        _viewModel.CollapseAllCommand.Execute(null);
        Assert.That(folder.IsExpanded, Is.False);
        Assert.That(_root.IsExpanded, Is.True);

        _viewModel.ExpandAllCommand.Execute(null);
        Assert.That(folder.IsExpanded, Is.True);
    }

    [Test]
    public void SortSortsByNameAndThenTheOtherWayAround()
    {
        string[] names = ["b", "c", "a"];
        for (int i = 0; i < names.Length; i++)
            _root.Children[i].Connection.Name = names[i];

        _viewModel.SortCommand.Execute(null);
        Assert.That(_root.Children.Select(node => node.Name), Is.EqualTo(new[] { "a", "b", "c" }));
        Assert.That(_viewModel.IsNextSortAscending, Is.False);

        _viewModel.SortCommand.Execute(null);
        Assert.That(_root.Children.Select(node => node.Name), Is.EqualTo(new[] { "c", "b", "a" }));
    }

    [Test]
    public void FavoritesAreTheConnectionsMarkedFavorite()
    {
        ConnectionInfo favorite = _root.Children[2].Connection;
        favorite.Favorite = true;
        _root.Children[0].Connection.Favorite = true; // a folder is never a favorite

        _viewModel.RefreshFavorites();

        Assert.That(_viewModel.Favorites.Select(node => node.Connection), Is.EqualTo(new[] { favorite }));
    }
}
