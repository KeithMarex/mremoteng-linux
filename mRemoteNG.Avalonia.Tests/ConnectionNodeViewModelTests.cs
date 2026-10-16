using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class ConnectionNodeViewModelTests
{
    [Test]
    public void FollowsChildrenBeingAddedAndRemoved()
    {
        RootNodeInfo root = new(RootNodeType.Connection);
        ConnectionNodeViewModel node = new(root);
        ConnectionInfo connection = new() { Name = "a" };

        root.AddChild(connection);
        Assert.That(node.Children.Select(child => child.Name), Is.EqualTo(new[] { "a" }));

        root.RemoveChild(connection);
        Assert.That(node.Children, Is.Empty);
    }

    [Test]
    public void KeepsExistingChildNodesWhenSiblingsChange()
    {
        RootNodeInfo root = new(RootNodeType.Connection);
        ContainerInfo folder = new() { Name = "folder" };
        root.AddChild(folder);
        ConnectionNodeViewModel node = new(root);
        ConnectionNodeViewModel folderNode = node.Children.Single();

        root.AddChild(new ConnectionInfo());

        Assert.That(node.Children.First(), Is.SameAs(folderNode));
    }

    [Test]
    public void ReflectsRenames()
    {
        ConnectionInfo connection = new() { Name = "old" };
        ConnectionNodeViewModel node = new(connection);
        List<string?> changed = [];
        node.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        connection.Name = "new";

        Assert.That(node.Name, Is.EqualTo("new"));
        Assert.That(changed, Does.Contain(nameof(ConnectionNodeViewModel.Name)));
    }

    [Test]
    public void NewItemsGoIntoTheSelectedFolderOrBesideTheSelectedConnection()
    {
        RootNodeInfo root = new(RootNodeType.Connection);
        ContainerInfo folder = new();
        root.AddChild(folder);
        folder.AddChild(new ConnectionInfo());
        ConnectionNodeViewModel rootNode = new(root);
        ConnectionNodeViewModel folderNode = rootNode.Children.Single();

        Assert.That(folderNode.TargetFolder, Is.SameAs(folderNode));
        Assert.That(folderNode.Children.Single().TargetFolder, Is.SameAs(folderNode));
    }
}

public class ConnectionTreeSearchTests
{
    private RootNodeInfo _root = null!;
    private ContainerInfo _web = null!;
    private ConnectionNodeViewModel _rootNode = null!;

    [SetUp]
    public void Setup()
    {
        _root = new RootNodeInfo(RootNodeType.Connection);
        _web = new ContainerInfo { Name = "web", IsExpanded = false };
        ContainerInfo db = new() { Name = "databases" };
        _root.AddChild(_web);
        _root.AddChild(db);
        _web.AddChild(new ConnectionInfo { Name = "frontend", Hostname = "fe01.example.org" });
        db.AddChild(new ConnectionInfo { Name = "postgres", Hostname = "pg01.example.org" });
        _rootNode = new ConnectionNodeViewModel(_root);
    }

    [Test]
    public void OnlyMatchesAndTheirFoldersStayVisible()
    {
        _rootNode.ApplyFilter("front");

        Assert.That(VisibleNames(), Is.EqualTo(new[] { "Connections", "web", "frontend" }));
    }

    [Test]
    public void HostNamesMatchToo()
    {
        _rootNode.ApplyFilter("PG01");

        Assert.That(VisibleNames(), Does.Contain("postgres").And.Not.Contain("frontend"));
    }

    [Test]
    public void FoldersWithMatchesOpenWithoutChangingTheFile()
    {
        ConnectionNodeViewModel webNode = _rootNode.Children.First();

        _rootNode.ApplyFilter("front");

        Assert.That(webNode.IsExpanded, Is.True);
        Assert.That(_web.IsExpanded, Is.False);
    }

    [Test]
    public void ClearingTheSearchShowsEverythingAsBefore()
    {
        _rootNode.ApplyFilter("front");
        _rootNode.ApplyFilter("");

        Assert.That(VisibleNames(), Has.Length.EqualTo(5));
        Assert.That(_rootNode.Children.First().IsExpanded, Is.False);
    }

    private string[] VisibleNames() =>
        _rootNode.SelfAndDescendants().Where(node => node.IsVisible).Select(node => node.Name).ToArray();
}
