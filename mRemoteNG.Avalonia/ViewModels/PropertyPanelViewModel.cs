using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Avalonia.Views;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Avalonia.ViewModels;

/// <summary>A category of the property grid; it can be collapsed, as on Windows.</summary>
public sealed partial class PropertyGroupViewModel(string category, IReadOnlyList<PropertyItemViewModel> items, bool showHeader)
    : ObservableObject
{
    public string Category { get; } = category;
    public IReadOnlyList<PropertyItemViewModel> Items { get; } = items;

    /// <summary>False in the alphabetical view, which has one group without a header.</summary>
    public bool ShowHeader { get; } = showHeader;

    [ObservableProperty]
    private bool _isExpanded = true;

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}

/// <summary>What the property grid shows, as the Properties and Inheritance buttons of Windows' Config panel.</summary>
public enum PropertyPanelMode
{
    Properties,
    Inheritance
}

/// <summary>
/// The Config panel: the selected connection's properties, grouped by category (or alphabetical), or its inheritance
/// settings. Which properties are shown follows Core's <see cref="ConnectionPropertyRules"/>, the rules of the
/// WinForms property grid: inherited properties are left out of the Properties view, as on Windows.
/// </summary>
public sealed partial class PropertyPanelViewModel : ObservableObject
{
    private readonly HashSet<string> _collapsedCategories = [];
    private HashSet<string> _shownProperties = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowInheritance))]
    [NotifyCanExecuteChangedFor(nameof(ShowInheritanceCommand))]
    private ConnectionInfo? _connection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPropertiesView), nameof(IsInheritanceView))]
    private PropertyPanelMode _mode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCategorized))]
    private bool _isAlphabetical;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DescriptionTitle), nameof(DescriptionText))]
    private PropertyItemViewModel? _selectedItem;

    /// <summary>Whether the description box shows under the grid (View menu).</summary>
    [ObservableProperty]
    private bool _isDescriptionVisible;

    public ObservableCollection<PropertyGroupViewModel> Groups { get; } = [];

    public bool IsCategorized => !IsAlphabetical;
    public bool IsPropertiesView => Mode == PropertyPanelMode.Properties;
    public bool IsInheritanceView => Mode == PropertyPanelMode.Inheritance;

    /// <summary>Only a connection or folder inside a folder can inherit.</summary>
    public bool CanShowInheritance => Connection is { } connection and not RootNodeInfo && connection.Inheritance.InheritanceActive;

    /// <summary>The selected row's name and description, shown under the grid as on Windows.</summary>
    public string DescriptionTitle => SelectedItem?.DisplayName ?? string.Empty;
    public string DescriptionText => SelectedItem?.Description ?? string.Empty;

    /// <summary>Raised after the user changed a value.</summary>
    public event EventHandler<PropertyItemViewModel>? PropertyEdited;

    /// <summary>The external tools' names, offered for ExtApp, PreExtApp and PostExtApp; set by the main window.</summary>
    public Func<IReadOnlyList<string>> ExternalToolNames { get; set; } = () => [];

    /// <summary>The SSH connections' names, offered for the SSH Tunnel; set by the main window.</summary>
    public Func<IReadOnlyList<string>> SshTunnelNames { get; set; } = () => [];

    partial void OnConnectionChanged(ConnectionInfo? value)
    {
        if (Mode == PropertyPanelMode.Inheritance && !CanShowInheritance)
            Mode = PropertyPanelMode.Properties; // rebuilds
        else
            Rebuild();
    }

    partial void OnModeChanged(PropertyPanelMode value) => Rebuild();

    partial void OnIsAlphabeticalChanged(bool value) => Rebuild();

    partial void OnSelectedItemChanged(PropertyItemViewModel? oldValue, PropertyItemViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsSelected = false;
        if (newValue is not null)
            newValue.IsSelected = true;
    }

    [RelayCommand]
    private void ShowCategorized() => IsAlphabetical = false;

    [RelayCommand]
    private void ShowAlphabetical() => IsAlphabetical = true;

    [RelayCommand]
    private void ShowProperties() => Mode = PropertyPanelMode.Properties;

    [RelayCommand(CanExecute = nameof(CanShowInheritance))]
    private void ShowInheritance() => Mode = PropertyPanelMode.Inheritance;

    private void Rebuild()
    {
        string? selectedName = SelectedItem?.Name;
        foreach (PropertyGroupViewModel group in Groups)
            group.PropertyChanged -= OnGroupPropertyChanged;
        Groups.Clear();
        SelectedItem = null;
        _shownProperties = [];
        if (Connection is not { } connection)
            return;

        object component = Mode == PropertyPanelMode.Inheritance ? connection.Inheritance : connection;
        List<PropertyItemViewModel> items = VisibleProperties(connection)
            .Select(property => new PropertyItemViewModel(component, property, OnItemChanged, AppImages.ConnectionIconNames,
                                                          ExternalToolNames(), SshTunnelNames()))
            .ToList();
        _shownProperties = items.Select(item => item.Name).ToHashSet();

        if (IsAlphabetical)
        {
            Groups.Add(new PropertyGroupViewModel(string.Empty,
                                                  items.OrderBy(item => item.DisplayName, StringComparer.CurrentCulture).ToList(),
                                                  showHeader: false));
        }
        else
        {
            // Within a category the properties keep their declared order, as with Windows' PropertySort.Categorized.
            foreach (IGrouping<string, PropertyItemViewModel> group in items
                         .OrderBy(item => item.CategorySortKey, StringComparer.Ordinal)
                         .GroupBy(item => item.Category))
            {
                PropertyGroupViewModel groupViewModel = new(group.Key, group.ToList(), showHeader: true)
                {
                    IsExpanded = !_collapsedCategories.Contains(group.Key)
                };
                groupViewModel.PropertyChanged += OnGroupPropertyChanged;
                Groups.Add(groupViewModel);
            }
        }

        SelectedItem = items.FirstOrDefault(item => item.Name == selectedName);
    }

    // A collapsed category stays collapsed for the next connection, as in the Windows property grid.
    private void OnGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PropertyGroupViewModel.IsExpanded) || sender is not PropertyGroupViewModel group)
            return;
        if (group.IsExpanded)
            _collapsedCategories.Remove(group.Category);
        else
            _collapsedCategories.Add(group.Category);
    }

    private IEnumerable<PropertyDescriptor> VisibleProperties(ConnectionInfo connection)
    {
        if (Mode == PropertyPanelMode.Inheritance)
        {
            // Every inheritance setting, whatever the protocol (as the Windows grid's Inheritance view).
            return TypeDescriptor.GetProperties(connection.Inheritance).Cast<PropertyDescriptor>()
                                 .Where(property => ConnectionPropertyRules.IsValidForProtocol(property, connection.Protocol, skipProtocolCheck: true));
        }

        IEnumerable<PropertyDescriptor> properties = TypeDescriptor.GetProperties(connection).Cast<PropertyDescriptor>();
        if (connection is RootNodeInfo rootNode)
        {
            string[] rootProperties = ConnectionPropertyRules.RootNodeProperties(rootNode);
            return properties.Where(property => rootProperties.Contains(property.Name));
        }

        HashSet<string> hidden = ConnectionPropertyRules.HiddenConnectionProperties(connection).ToHashSet();
        return properties.Where(property =>
                             ConnectionPropertyRules.IsValidForProtocol(property, connection.Protocol, skipProtocolCheck: false) &&
                             !hidden.Contains(property.Name))
                         .Concat(FileTransferProperties.For(connection))
                         .Concat(SpiceProperties.For(connection));
    }

    private static int FtpPortFor(bool implicitTls) => implicitTls ? 990 : DefaultPorts.Ftp;

    private void OnItemChanged(PropertyItemViewModel item)
    {
        ConnectionInfo connection = Connection!;
        switch (item.Name)
        {
            // Same follow-up edits as the WinForms property grid.
            case nameof(ConnectionInfo.Protocol) when !item.IsInheritanceSetting:
                connection.SetDefaultPort();
                break;
            case nameof(ConnectionInfo.Name) when !item.IsInheritanceSetting && CoreRuntime.Settings.SetHostnameLikeDisplayName
                                                  && !string.IsNullOrEmpty(connection.Name):
                connection.Hostname = connection.Name;
                break;
            // Implicit TLS has a port of its own.
            case FileTransferSettings.EncryptionKey when connection.Port == FtpPortFor(implicitTls: false) || connection.Port == FtpPortFor(implicitTls: true):
                connection.Port = FtpPortFor(FileTransferSettings.Encryption(connection) == FileTransfer.Ftp.FtpEncryption.Implicit);
                break;
            // A Proxmox VE console starts at the API's port, a direct one at QEMU's.
            case SpiceSettings.SourceKey when connection.Port is DefaultPorts.Proxmox or SpiceSettings.DirectPort:
                connection.Port = SpiceSettings.Source(connection) == SpiceSource.Proxmox ? DefaultPorts.Proxmox : SpiceSettings.DirectPort;
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
