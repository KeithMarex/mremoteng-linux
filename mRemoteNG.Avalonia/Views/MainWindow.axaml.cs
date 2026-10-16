using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Tree;

namespace mRemoteNG.Avalonia.Views;

public partial class MainWindow : Window
{
    private static readonly DataFormat<ConnectionNodeViewModel> NodeFormat =
        DataFormat.CreateInProcessFormat<ConnectionNodeViewModel>("mremoteng-connection-node");

    private const double DragThreshold = 4;

    private bool _closeConfirmed;
    private PointerPressedEventArgs? _dragStart;
    private Point _dragStartPoint;

    public MainWindow()
    {
        InitializeComponent();
        ConnectionTree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel);
        ConnectionTree.AddHandler(PointerMovedEvent, OnTreePointerMoved, RoutingStrategies.Tunnel);
        ConnectionTree.AddHandler(PointerReleasedEvent, (_, _) => _dragStart = null, RoutingStrategies.Tunnel);
        DragDrop.AddDragOverHandler(ConnectionTree, OnTreeDragOver);
        DragDrop.AddDropHandler(ConnectionTree, OnTreeDrop);
        ConnectionTree.DoubleTapped += OnTreeDoubleTapped;
        AddHandler(KeyDownEvent, OnMenuShortcut, RoutingStrategies.Bubble);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_closeConfirmed || ViewModel is not { } viewModel)
        {
            base.OnClosing(e);
            return;
        }

        // Ask asynchronously (saving or confirming unsaved changes), then close for real.
        e.Cancel = true;
        if (await viewModel.CanCloseAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    // The favorite connections, as the dropdown of Windows' Favorites button; choosing one opens it.
    private void OnFavoritesClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        viewModel.RefreshFavorites();
        MenuFlyout menu = new();
        foreach (ConnectionNodeViewModel favorite in viewModel.Favorites)
        {
            menu.Items.Add(new MenuItem
            {
                Header = favorite.Name,
                Icon = new Image { Source = favorite.Icon, Width = 16, Height = 16 },
                Command = viewModel.OpenFavoriteCommand,
                CommandParameter = favorite
            });
        }

        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = UiText.NoFavorites, IsEnabled = false });
        menu.ShowAt(FavoritesButton);
    }

    /// <summary>
    /// Runs the menu item whose shortcut (with Ctrl or Alt) was pressed. Avalonia applies HotKeys before the focused
    /// control sees the key, so a terminal could not keep Ctrl+Q (XON), Ctrl+S (XOFF) or Ctrl+D (end of input) for
    /// its program; here the focused control goes first, and a session's terminal claims every key typed in it.
    /// </summary>
    private void OnMenuShortcut(object? sender, KeyEventArgs e)
    {
        if (e.Handled || (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == 0)
            return;

        MenuItem? item = MainMenu.GetLogicalDescendants().OfType<MenuItem>()
                                 .FirstOrDefault(menuItem => menuItem.InputGesture?.Matches(e) == true);
        if (item is null)
            return;

        e.Handled = true;
        if (item.Command is { } command)
        {
            if (command.CanExecute(item.CommandParameter))
                command.Execute(item.CommandParameter);
        }
        else
        {
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
    }

    // Double-clicking a connection opens it; on a folder it keeps toggling the folder.
    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ItemAt(e.Source)?.DataContext is ConnectionNodeViewModel { IsFolder: false } node && ViewModel is { } viewModel)
        {
            viewModel.SelectedNode = node;
            viewModel.ConnectCommand.Execute(null);
        }
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragStart = e.GetCurrentPoint(ConnectionTree).Properties.IsLeftButtonPressed ? e : null;
        _dragStartPoint = e.GetPosition(ConnectionTree);
    }

    private async void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not { } start)
            return;

        Vector moved = e.GetPosition(ConnectionTree) - _dragStartPoint;
        if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold)
            return;

        _dragStart = null;
        if (ItemAt(start.Source)?.DataContext is not ConnectionNodeViewModel { IsRoot: false } node)
            return;

        DataTransfer data = new();
        data.Add(DataTransferItem.Create(NodeFormat, node));
        await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Move);
    }

    private void OnTreeDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DropTargetOf(e) is { } drop && ViewModel!.CanMove(drop.Source, drop.Target, drop.Position)
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private void OnTreeDrop(object? sender, DragEventArgs e)
    {
        if (DropTargetOf(e) is { } drop)
            ViewModel?.Move(drop.Source, drop.Target, drop.Position);
    }

    private (ConnectionNodeViewModel Source, ConnectionNodeViewModel Target, MovePosition Position)? DropTargetOf(DragEventArgs e)
    {
        if (ViewModel is null || e.DataTransfer.TryGetValue(NodeFormat) is not { } source)
            return null;
        if (ItemAt(e.Source) is not { DataContext: ConnectionNodeViewModel target } item)
            return null;

        // Measure within the item's own row; an expanded item's bounds also contain its children.
        Visual row = item.GetVisualDescendants().OfType<Control>()
                         .FirstOrDefault(control => control.Name is "PART_HeaderPresenter" or "PART_Header"
                                                    && control.FindAncestorOfType<TreeViewItem>() == item)
                     ?? item;
        double y = e.GetPosition(row).Y;
        double height = Math.Max(row.Bounds.Height, 1);
        MovePosition position = target.IsFolder && y > height * 0.25 && y < height * 0.75
            ? MovePosition.Onto
            : y < height / 2 ? MovePosition.Above : MovePosition.Below;
        return (source, target, position);
    }

    private static TreeViewItem? ItemAt(object? source) =>
        (source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);
}
