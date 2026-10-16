using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Avalonia.ViewModels;

/// <summary>A node of the connection tree, kept in sync with the connection or folder it wraps.</summary>
public sealed class ConnectionNodeViewModel : ObservableObject, IDisposable
{
    private bool _isVisible = true;
    private bool _expandedByFilter;

    public ConnectionNodeViewModel(ConnectionInfo connection, ConnectionNodeViewModel? parent = null)
    {
        Connection = connection;
        Parent = parent;
        connection.PropertyChanged += OnConnectionPropertyChanged;
        if (connection is ContainerInfo container)
        {
            container.CollectionChanged += OnChildrenChanged;
            RebuildChildren(container);
        }
    }

    public ConnectionInfo Connection { get; }

    public ConnectionNodeViewModel? Parent { get; }

    public ObservableCollection<ConnectionNodeViewModel> Children { get; } = [];

    public string Name => Connection.Name;

    public bool IsFolder => Connection is ContainerInfo;

    public bool IsRoot => Connection is RootNodeInfo;

    /// <summary>Whether the connection has an open session (shown in bold).</summary>
    public bool IsConnected => Connection.OpenConnections.Count > 0;

    /// <summary>The protocol shown next to a connection's name; empty for folders.</summary>
    public string Detail => IsFolder ? string.Empty : Connection.Protocol.ToString();

    /// <summary>
    /// Expanded state, stored in the connection file. While a search shows matches inside a collapsed
    /// folder, the folder is opened in the view only, so searching does not change the file.
    /// </summary>
    public bool IsExpanded
    {
        get => _expandedByFilter || Connection is ContainerInfo { IsExpanded: true };
        set
        {
            if (Connection is not ContainerInfo container || value == IsExpanded)
                return;

            if (!value)
                _expandedByFilter = false;
            container.IsExpanded = value;
            OnPropertyChanged();
        }
    }

    /// <summary>False while a search is active and neither this node nor anything below it matches.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    /// <summary>
    /// Shows only nodes whose name, host name or description contains <paramref name="searchText"/>, plus
    /// the folders leading to them (opened); an empty text shows everything again. Returns whether this node
    /// or anything below it matches.
    /// </summary>
    public bool ApplyFilter(string? searchText)
    {
        string text = searchText?.Trim() ?? string.Empty;
        bool childMatches = false;
        foreach (ConnectionNodeViewModel child in Children)
            childMatches |= child.ApplyFilter(text);

        if (text.Length == 0)
        {
            IsVisible = true;
            SetExpandedByFilter(false);
            return true;
        }

        bool matches = childMatches || Matches(text);
        IsVisible = matches || IsRoot;
        SetExpandedByFilter(childMatches);
        return matches;
    }

    private void SetExpandedByFilter(bool expanded)
    {
        bool wasExpanded = IsExpanded;
        _expandedByFilter = expanded;
        if (IsExpanded != wasExpanded)
            OnPropertyChanged(nameof(IsExpanded));
    }

    private bool Matches(string text) =>
        Connection.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase) ||
        (!IsFolder && Connection.Hostname.Contains(text, StringComparison.CurrentCultureIgnoreCase)) ||
        Connection.Description.Contains(text, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>The folder new items are added to when this node is selected.</summary>
    public ConnectionNodeViewModel TargetFolder => IsFolder ? this : Parent ?? this;

    public IEnumerable<ConnectionNodeViewModel> SelfAndDescendants()
    {
        yield return this;
        foreach (ConnectionNodeViewModel descendant in Children.SelectMany(child => child.SelfAndDescendants()))
            yield return descendant;
    }

    public void Dispose()
    {
        Connection.PropertyChanged -= OnConnectionPropertyChanged;
        if (Connection is ContainerInfo container)
            container.CollectionChanged -= OnChildrenChanged;
        foreach (ConnectionNodeViewModel child in Children)
            child.Dispose();
    }

    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Containers also relay their children's changes; only react to this node's own.
        if (!ReferenceEquals(sender, Connection))
            return;

        switch (e.PropertyName)
        {
            case nameof(ConnectionInfo.Name):
                OnPropertyChanged(nameof(Name));
                break;
            case nameof(ConnectionInfo.Protocol):
                OnPropertyChanged(nameof(Detail));
                break;
            case nameof(ConnectionInfo.OpenConnections):
                OnPropertyChanged(nameof(IsConnected));
                break;
        }
    }

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, Connection))
            RebuildChildren((ContainerInfo)Connection);
    }

    private void RebuildChildren(ContainerInfo container)
    {
        Dictionary<ConnectionInfo, ConnectionNodeViewModel> existing = Children.ToDictionary(child => child.Connection);
        Children.Clear();
        foreach (ConnectionInfo child in container.Children)
        {
            if (existing.Remove(child, out ConnectionNodeViewModel? node))
                Children.Add(node);
            else
                Children.Add(new ConnectionNodeViewModel(child, this));
        }

        foreach (ConnectionNodeViewModel removed in existing.Values)
            removed.Dispose();
    }
}
