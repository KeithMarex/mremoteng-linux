using System.ComponentModel;
using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Container;
using mRemoteNG.Resources.Language;
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
    public void PropertiesAreGroupedByCategoryInTheirDeclaredOrder()
    {
        _panel.Connection = new ConnectionInfo { Protocol = ProtocolType.SSH2 };

        Assert.That(_panel.Groups.Select(group => group.Category), Is.Unique);
        Assert.That(_panel.Groups.All(group => group.ShowHeader), Is.True);
        Assert.That(_panel.Groups[0].Items.Select(item => item.Name).Take(3),
                    Is.EqualTo(new[] { nameof(ConnectionInfo.Name), nameof(ConnectionInfo.Description), nameof(ConnectionInfo.Icon) }));
    }

    [Test]
    public void TheAlphabeticalViewIsOneSortedListWithoutHeader()
    {
        _panel.Connection = new ConnectionInfo { Protocol = ProtocolType.SSH2 };
        int count = ShownProperties().Count();

        _panel.ShowAlphabeticalCommand.Execute(null);

        PropertyGroupViewModel group = _panel.Groups.Single();
        Assert.That(group.ShowHeader, Is.False);
        Assert.That(group.Items, Has.Count.EqualTo(count));
        Assert.That(group.Items.Select(item => item.DisplayName), Is.Ordered.Using((IComparer<string>)StringComparer.CurrentCulture));
    }

    [Test]
    public void ACollapsedCategoryStaysCollapsedForTheNextConnection()
    {
        _panel.Connection = new ConnectionInfo { Protocol = ProtocolType.SSH2 };
        string category = _panel.Groups[0].Category;

        _panel.Groups[0].ToggleCommand.Execute(null);
        _panel.Connection = new ConnectionInfo { Protocol = ProtocolType.Telnet };

        Assert.That(_panel.Groups.Where(group => !group.IsExpanded).Select(group => group.Category), Is.EqualTo(new[] { category }));
    }

    [Test]
    public void InheritedPropertiesAreLeftOutOfThePropertiesView()
    {
        ConnectionInfo connection = ConnectionInAFolder();
        connection.Inheritance.Username = true;

        _panel.Connection = connection;

        Assert.That(ShownProperties(), Does.Not.Contain(nameof(ConnectionInfo.Username)));
        Assert.That(ShownProperties(), Does.Contain(nameof(ConnectionInfo.Hostname)));
    }

    [Test]
    public void TheInheritanceViewEditsWhatIsInherited()
    {
        ConnectionInfo connection = ConnectionInAFolder();
        _panel.Connection = connection;

        _panel.ShowInheritanceCommand.Execute(null);
        PropertyItemViewModel username = Item(nameof(ConnectionInfoInheritance.Username));
        username.SelectedChoice = Choice(username, true);

        Assert.That(username.IsInheritanceSetting, Is.True);
        Assert.That(connection.Inheritance.Username, Is.True);
        _panel.ShowPropertiesCommand.Execute(null);
        Assert.That(ShownProperties(), Does.Not.Contain(nameof(ConnectionInfo.Username)));
    }

    [Test]
    public void OnlyWhatIsInAFolderHasAnInheritanceView()
    {
        ConnectionInfo topLevel = new() { Protocol = ProtocolType.SSH2 };
        new RootNodeInfo(RootNodeType.Connection).AddChild(topLevel);
        _panel.Connection = ConnectionInAFolder();
        _panel.ShowInheritanceCommand.Execute(null);

        _panel.Connection = topLevel;

        Assert.That(_panel.ShowInheritanceCommand.CanExecute(null), Is.False);
        Assert.That(_panel.IsPropertiesView, Is.True);
        Assert.That(ShownProperties(), Does.Contain(nameof(ConnectionInfo.Hostname)));
    }

    [Test]
    public void BooleansAreYesOrNo()
    {
        ConnectionInfo connection = new() { Protocol = ProtocolType.SSH2 };
        _panel.Connection = connection;
        PropertyItemViewModel favorite = Item(nameof(ConnectionInfo.Favorite));

        favorite.SelectedChoice = Choice(favorite, true);

        Assert.That(favorite.Choices.Select(choice => choice.DisplayText), Is.EqualTo(new[] { Language.Yes, Language.No }));
        Assert.That(connection.Favorite, Is.True);
    }

    [Test]
    public void TheIconIsChosenFromTheAppsIconsAndKeepsAnUnknownOne()
    {
        ConnectionInfo connection = new() { Icon = "Custom" };
        PropertyDescriptor icon = TypeDescriptor.GetProperties(connection)[nameof(ConnectionInfo.Icon)]!;
        PropertyItemViewModel item = new(connection, icon, _ => { }, ["Linux", "mRemoteNG"]);

        item.SelectedChoice = Choice(item, "Linux");

        Assert.That(item.Choices.Select(choice => choice.DisplayText), Is.EqualTo(new[] { "Linux", "mRemoteNG", "Custom" }));
        Assert.That(connection.Icon, Is.EqualTo("Linux"));
    }

    [Test]
    public void TheSelectedRowsDescriptionIsShown()
    {
        _panel.Connection = new ConnectionInfo { Protocol = ProtocolType.SSH2 };
        PropertyItemViewModel hostname = Item(nameof(ConnectionInfo.Hostname));

        _panel.SelectedItem = hostname;

        Assert.That(hostname.IsSelected, Is.True);
        Assert.That(_panel.DescriptionTitle, Is.EqualTo(hostname.DisplayName));
        Assert.That(_panel.DescriptionText, Is.EqualTo(hostname.Description).And.Not.Empty);
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

    private static ConnectionInfo ConnectionInAFolder()
    {
        ContainerInfo folder = new();
        ConnectionInfo connection = new() { Protocol = ProtocolType.SSH2 };
        new RootNodeInfo(RootNodeType.Connection).AddChild(folder);
        folder.AddChild(connection);
        return connection;
    }

    private static PropertyChoice Choice(PropertyItemViewModel item, object value) => item.Choices.Single(choice => Equals(choice.Value, value));

    private IEnumerable<string> ShownProperties() => _panel.Groups.SelectMany(group => group.Items).Select(item => item.Name);

    private PropertyItemViewModel Item(string name) => _panel.Groups.SelectMany(group => group.Items).Single(item => item.Name == name);
}
