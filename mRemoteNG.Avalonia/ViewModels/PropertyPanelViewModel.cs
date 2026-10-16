using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Avalonia.ViewModels;

public sealed record PropertyGroupViewModel(string Category, IReadOnlyList<PropertyItemViewModel> Items);

/// <summary>
/// The properties of the selected connection, grouped by category. Which ones are shown follows Core's
/// <see cref="ConnectionPropertyRules"/>, the same rules as the WinForms property grid; inherited properties
/// stay visible but read-only.
/// </summary>
public sealed partial class PropertyPanelViewModel : ObservableObject
{
    private HashSet<string> _shownProperties = [];

    [ObservableProperty]
    private ConnectionInfo? _connection;

    public ObservableCollection<PropertyGroupViewModel> Groups { get; } = [];

    /// <summary>Raised after the user changed a value.</summary>
    public event EventHandler<PropertyItemViewModel>? PropertyEdited;

    partial void OnConnectionChanged(ConnectionInfo? value) => Rebuild();

    private void Rebuild()
    {
        Groups.Clear();
        _shownProperties = [];
        if (Connection is null)
            return;

        List<PropertyItemViewModel> items = VisibleProperties(Connection)
            .Select(property => new PropertyItemViewModel(Connection, property, OnItemChanged))
            .ToList();
        _shownProperties = items.Select(item => item.Name).ToHashSet();

        foreach (IGrouping<string, PropertyItemViewModel> group in items
                     .OrderBy(item => item.CategorySortKey, StringComparer.Ordinal)
                     .ThenBy(item => item.DisplayName, StringComparer.CurrentCulture)
                     .GroupBy(item => item.Category))
        {
            Groups.Add(new PropertyGroupViewModel(group.Key, group.ToList()));
        }
    }

    private static IEnumerable<PropertyDescriptor> VisibleProperties(ConnectionInfo connection)
    {
        IEnumerable<PropertyDescriptor> properties = TypeDescriptor.GetProperties(connection).Cast<PropertyDescriptor>();

        if (connection is RootNodeInfo rootNode)
        {
            string[] rootProperties = ConnectionPropertyRules.RootNodeProperties(rootNode);
            return properties.Where(property => rootProperties.Contains(property.Name));
        }

        HashSet<string> hidden = ConnectionPropertyRules.HiddenConnectionProperties(connection, hideInherited: false).ToHashSet();
        return properties.Where(property =>
                             ConnectionPropertyRules.IsValidForProtocol(property, connection.Protocol, skipProtocolCheck: false) &&
                             !hidden.Contains(property.Name))
                         .Concat(FileTransferProperties.For(connection));
    }

    private static int FtpPortFor(bool implicitTls) => implicitTls ? 990 : DefaultPorts.Ftp;

    private void OnItemChanged(PropertyItemViewModel item)
    {
        ConnectionInfo connection = Connection!;
        switch (item.Name)
        {
            // Same follow-up edits as the WinForms property grid.
            case nameof(ConnectionInfo.Protocol):
                connection.SetDefaultPort();
                break;
            // Implicit TLS has a port of its own.
            case FileTransferSettings.EncryptionKey when connection.Port == FtpPortFor(implicitTls: false) || connection.Port == FtpPortFor(implicitTls: true):
                connection.Port = FtpPortFor(FileTransferSettings.Encryption(connection) == FileTransfer.Ftp.FtpEncryption.Implicit);
                break;
            case nameof(ConnectionInfo.Name) when CoreRuntime.Settings.SetHostnameLikeDisplayName
                                                  && !string.IsNullOrEmpty(connection.Name):
                connection.Hostname = connection.Name;
                break;
        }

        PropertyEdited?.Invoke(this, item);

        // Recreate the editors only when the set of applicable properties changed, so typing in
        // one field never loses focus because of another.
        if (VisibleProperties(connection).Select(property => property.Name).ToHashSet().SetEquals(_shownProperties))
        {
            foreach (PropertyItemViewModel other in Groups.SelectMany(group => group.Items))
                other.RefreshValue();
        }
        else
        {
            Rebuild();
        }
    }
}
