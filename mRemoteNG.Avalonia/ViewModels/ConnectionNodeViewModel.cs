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

    /// <summary>The protocol shown next to a connection's name; empty for folders.</summary>
    public string Detail => IsFolder ? string.Empty : Connection.Protocol.ToString();

    public bool IsExpanded
    {
        get => Connection is ContainerInfo { IsExpanded: true };
        set
        {
            if (Connection is not ContainerInfo container || container.IsExpanded == value)
                return;
            container.IsExpanded = value;
            OnPropertyChanged();
        }
    }

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
