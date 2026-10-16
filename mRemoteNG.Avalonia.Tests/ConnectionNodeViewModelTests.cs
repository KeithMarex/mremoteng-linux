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
