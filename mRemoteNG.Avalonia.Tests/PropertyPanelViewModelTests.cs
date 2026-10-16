using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Container;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class PropertyPanelViewModelTests
{
    private readonly PropertyPanelViewModel _panel = new();

    [Test]
    public void RootNodeShowsOnlyNameAndPasswordProtection()
    {
        _panel.Connection = new RootNodeInfo(RootNodeType.Connection);

        Assert.That(ShownProperties(), Is.EquivalentTo(new[] { "Name", "Password" }));
    }

    [Test]
    public void ShowsOnlyPropertiesOfTheConnectionsProtocol()
    {
        _panel.Connection = new ConnectionInfo { Protocol = ProtocolType.SSH2 };

        Assert.That(ShownProperties(), Does.Contain(nameof(ConnectionInfo.Hostname)).And.Contain(nameof(ConnectionInfo.Username)));
        Assert.That(ShownProperties(), Does.Not.Contain(nameof(ConnectionInfo.RDGatewayHostname)));
    }

    [Test]
    public void ChangingTheProtocolSetsItsPortAndShowsItsProperties()
    {
        ConnectionInfo connection = new() { Protocol = ProtocolType.SSH2 };
        _panel.Connection = connection;
        PropertyItemViewModel protocol = Item(nameof(ConnectionInfo.Protocol));

        protocol.SelectedChoice = protocol.Choices.Single(choice => Equals(choice.Value, ProtocolType.RDP));

        Assert.That(connection.Port, Is.EqualTo(3389));
        Assert.That(ShownProperties(), Does.Contain(nameof(ConnectionInfo.RDGatewayUsageMethod)));
    }

    [Test]
    public void EditingTextUpdatesTheConnection()
    {
        ConnectionInfo connection = new() { Protocol = ProtocolType.SSH2 };
        _panel.Connection = connection;

        Item(nameof(ConnectionInfo.Hostname)).Text = "srv.example.org";

        Assert.That(connection.Hostname, Is.EqualTo("srv.example.org"));
    }

    [Test]
    public void InvalidNumbersAreIgnored()
    {
        ConnectionInfo connection = new() { Protocol = ProtocolType.SSH2, Port = 2222 };
        _panel.Connection = connection;

        Item(nameof(ConnectionInfo.Port)).Text = "not a port";

        Assert.That(connection.Port, Is.EqualTo(2222));
    }

    [Test]
    public void InheritedValuesAreShownReadOnly()
    {
        ContainerInfo parent = new() { Username = "admin" };
        ContainerInfo folder = new();
        ConnectionInfo connection = new() { Protocol = ProtocolType.SSH2 };
        new RootNodeInfo(RootNodeType.Connection).AddChild(parent);
        parent.AddChild(folder);
        folder.AddChild(connection);
        folder.Username = "admin";
        connection.Inheritance.Username = true;
        _panel.Connection = connection;

        PropertyItemViewModel username = Item(nameof(ConnectionInfo.Username));

        Assert.That(username.CanInherit && username.IsInherited, Is.True);
        Assert.That(username.IsEditable, Is.False);
        Assert.That(username.Text, Is.EqualTo("admin"));
    }

    [Test]
    public void EditsAreReported()
    {
        _panel.Connection = new ConnectionInfo { Protocol = ProtocolType.SSH2 };
        List<string> edited = [];
        _panel.PropertyEdited += (_, item) => edited.Add(item.Name);

        Item(nameof(ConnectionInfo.Description)).Text = "web server";

        Assert.That(edited, Is.EqualTo(new[] { nameof(ConnectionInfo.Description) }));
    }

    private IEnumerable<string> ShownProperties() => _panel.Groups.SelectMany(group => group.Items).Select(item => item.Name);

    private PropertyItemViewModel Item(string name) => _panel.Groups.SelectMany(group => group.Items).Single(item => item.Name == name);
}
