using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mRemoteNG.Container;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Avalonia.ViewModels;

/// <summary>The Connections panel's toolbar, as on Windows: expand and collapse all, sort, favorites.</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>The direction the next sort sorts in; the button shows it, and each click turns it around.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNextSortAscending))]
    private ListSortDirection _nextSortDirection = ListSortDirection.Ascending;

    public bool IsNextSortAscending => NextSortDirection == ListSortDirection.Ascending;

    /// <summary>The favorite connections, for the Favorites button's menu (refreshed when it opens).</summary>
    public ObservableCollection<ConnectionNodeViewModel> Favorites { get; } = [];

    [RelayCommand(CanExecute = nameof(HasFile))]
    private void ExpandAll()
    {
        foreach (ConnectionNodeViewModel node in AllNodes().Where(node => node.IsFolder))
            node.IsExpanded = true;
    }

    /// <summary>Collapses every folder; the root stays open so the tree does not look empty.</summary>
    [RelayCommand(CanExecute = nameof(HasFile))]
    private void CollapseAll()
    {
        foreach (ConnectionNodeViewModel node in AllNodes().Where(node => node.IsFolder && !node.IsRoot))
            node.IsExpanded = false;
    }

    /// <summary>Sorts the whole tree by name, A to Z and then Z to A, like Windows' sort button.</summary>
    [RelayCommand(CanExecute = nameof(HasFile))]
    private void Sort()
    {
        ConnectionNodeViewModel? selected = SelectedNode;
        foreach (ContainerInfo root in RootNodes.Select(node => node.Connection).OfType<RootNodeInfo>())
            root.SortRecursive(NextSortDirection);
        NextSortDirection = NextSortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        ApplySearch();
        if (selected is not null)
            SelectedNode = FindNode(selected.Connection);
    }

    /// <summary>Fills <see cref="Favorites"/> with the connections marked Favorite.</summary>
    public void RefreshFavorites()
    {
        Favorites.Clear();
        foreach (ConnectionNodeViewModel node in AllNodes().Where(node => !node.IsFolder && node.Connection.Favorite)
                                                           .OrderBy(node => node.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Favorites.Add(node);
        }
    }

    /// <summary>Opens a favorite from the Favorites menu.</summary>
    [RelayCommand]
    private async Task OpenFavoriteAsync(ConnectionNodeViewModel? node)
    {
        if (node is null)
            return;
        SelectedNode = node;
        await ConnectCommand.ExecuteAsync(null);
    }

    private IEnumerable<ConnectionNodeViewModel> AllNodes() => RootNodes.SelectMany(root => root.SelfAndDescendants());

    private void NotifyTreeCommands()
    {
        ExpandAllCommand.NotifyCanExecuteChanged();
        CollapseAllCommand.NotifyCanExecuteChanged();
        SortCommand.NotifyCanExecuteChanged();
    }
}
