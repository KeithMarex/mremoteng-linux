using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using mRemoteNG.Avalonia.ViewModels;

namespace mRemoteNG.Avalonia.Views;

/// <summary>
/// A file transfer tab. Dragging files from one pane to the other downloads or uploads them; files dragged in from a
/// file manager are uploaded. Dropped on a directory or on "..", they go there; within one pane that moves them, as in
/// FileZilla. Double-click (or Enter) opens a directory. Clicking a column header sorts by it; dragging the right edge of
/// a column header makes it wider or narrower.
/// </summary>
public partial class FileTransferView : UserControl
{
    private static readonly DataFormat<PaneDrag> PaneFormat = DataFormat.CreateInProcessFormat<PaneDrag>("mremoteng-file-pane");
    private const double DragThreshold = 4;

    private PointerPressedEventArgs? _dragStart;
    private Point _dragStartPoint;
    private ListBoxItem? _dropTarget;
    private FileColumnViewModel? _resizing;
    private double _resizeStartX;
    private double _resizeStartWidth;

    public FileTransferView()
    {
        InitializeComponent();
        foreach (ListBox list in new[] { LocalList, RemoteList })
        {
            list.DoubleTapped += OnListDoubleTapped;
            list.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
            list.AddHandler(PointerMovedEvent, OnListPointerMoved, RoutingStrategies.Tunnel);
            list.AddHandler(PointerReleasedEvent, (_, _) => _dragStart = null, RoutingStrategies.Tunnel);
            DragDrop.AddDragOverHandler(list, OnDragOver);
            DragDrop.AddDragLeaveHandler(list, (_, _) => ShowDropTarget(null));
            DragDrop.AddDropHandler(list, OnDrop);
        }

        foreach ((ListBox list, ScrollViewer header) in new[] { (LocalList, LocalHeader), (RemoteList, RemoteHeader) })
        {
            // The header scrolls sideways with its list, and only with it.
            list.AddHandler(ScrollViewer.ScrollChangedEvent, (_, e) =>
            {
                if (e.Source is ScrollViewer rows)
                    header.Offset = new Vector(rows.Offset.X, 0);
            });
            header.AddHandler(PointerWheelChangedEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel);
            // Sorting lists the entries anew, which takes the focus away: give it back, so F2, Delete and Enter keep working.
            header.AddHandler(Button.ClickEvent, (_, _) => Dispatcher.UIThread.Post(() => FocusList(list), DispatcherPriority.Background));
            header.AddHandler(PointerPressedEvent, OnHeaderPointerPressed, RoutingStrategies.Tunnel);
            header.AddHandler(PointerMovedEvent, OnHeaderPointerMoved, RoutingStrategies.Tunnel);
            header.AddHandler(PointerReleasedEvent, (_, e) => EndResize(header, e.Pointer), RoutingStrategies.Tunnel);
            header.AddHandler(PointerCaptureLostEvent, (_, e) => EndResize(header, e.Pointer));
        }
    }

    private FileTransferViewModel? ViewModel => DataContext as FileTransferViewModel;

    // Starts resizing the column whose right edge (its grip) is under the pointer.
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Control { DataContext: FileColumnViewModel column } grip || !grip.Classes.Contains("columnGrip") ||
            !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _resizing = column;
        _resizeStartX = e.GetPosition(this).X;
        _resizeStartWidth = column.Width;
        e.Pointer.Capture(grip);
        e.Handled = true;
    }

    private void OnHeaderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_resizing is not { } column)
            return;
        column.Width = _resizeStartWidth + e.GetPosition(this).X - _resizeStartX;
        e.Handled = true;
    }

    // Stores the new width once, when the drag ends.
    private void EndResize(ScrollViewer header, IPointer pointer)
    {
        if (_resizing is null)
            return;
        _resizing = null;
        pointer.Capture(null);
        if (ViewModel is { } viewModel)
            (header == RemoteHeader ? (FilePaneViewModel)viewModel.Remote : viewModel.Local).SaveLayout();
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (EntryAt(e.Source) is not { IsDirectory: true } entry || ViewModel is not { } viewModel)
            return;
        FilePaneViewModel pane = sender == RemoteList ? viewModel.Remote : viewModel.Local;
        pane.OpenCommand.Execute(entry);
    }

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragStart = e.GetCurrentPoint((Visual)sender!).Properties.IsLeftButtonPressed ? e : null;
        _dragStartPoint = e.GetPosition((Visual)sender!);
    }

    private async void OnListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not { } start || sender is not ListBox list)
            return;
        Vector moved = e.GetPosition(list) - _dragStartPoint;
        if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold)
            return;

        _dragStart = null;
        if (EntryAt(start.Source) is not { IsParent: false } pressed)
            return;

        // The selection, if the drag started on it; otherwise just the entry under the pointer.
        List<FileEntryViewModel> selected = list.SelectedItems?.OfType<FileEntryViewModel>().Where(entry => !entry.IsParent).ToList() ?? [];
        IReadOnlyList<FileEntryViewModel> entries = selected.Contains(pressed) ? selected : [pressed];
        DataTransfer data = new();
        data.Add(DataTransferItem.Create(PaneFormat, new PaneDrag(list == RemoteList, entries)));
        await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Copy | DragDropEffects.Move);
        ShowDropTarget(null);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool toRemote = sender == RemoteList;
        FileEntryViewModel? directory = DirectoryAt(e.Source);
        if (e.DataTransfer.TryGetValue(PaneFormat) is { } drag)
        {
            // Within a pane only onto a directory (or ".."), which moves; to the other pane it copies.
            bool move = drag.FromRemote == toRemote;
            FilePaneViewModel? pane = ViewModel is { } viewModel ? toRemote ? viewModel.Remote : viewModel.Local : null;
            bool canMove = move && directory is not null && pane is not null && drag.Entries.Any(entry => pane.CanMoveInto(entry, directory.FullPath));
            e.DragEffects = !move ? DragDropEffects.Copy : canMove ? DragDropEffects.Move : DragDropEffects.None;
        }
        else
        {
            e.DragEffects = toRemote && e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        ShowDropTarget(e.DragEffects != DragDropEffects.None && directory is not null ? ItemAt(e.Source) : null);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        ShowDropTarget(null);
        if (ViewModel is not { IsLive: true } viewModel)
            return;

        bool toRemote = sender == RemoteList;
        string? directory = DirectoryAt(e.Source)?.FullPath;
        if (e.DataTransfer.TryGetValue(PaneFormat) is { } drag)
        {
            if (drag.FromRemote == toRemote)
            {
                if (directory is not null)
                    await viewModel.MoveAsync(toRemote ? viewModel.Remote : viewModel.Local, drag.Entries, directory);
            }
            else if (drag.FromRemote)
            {
                await viewModel.DownloadAsync(drag.Entries, directory);
            }
            else
            {
                await viewModel.UploadAsync(drag.Entries.Select(entry => entry.FullPath).ToList(), directory);
            }
        }
        else if (toRemote && e.DataTransfer.TryGetFiles() is { } files)
        {
            await viewModel.UploadAsync(files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList(), directory);
        }
    }

    private static void FocusList(ListBox list)
    {
        if (list.SelectedItem is { } selected && list.ContainerFromItem(selected) is { } container)
            container.Focus();
        else
            list.Focus();
    }

    // Marks the directory a drop would go into.
    private void ShowDropTarget(ListBoxItem? item)
    {
        if (item == _dropTarget)
            return;
        _dropTarget?.Classes.Remove("dropTarget");
        _dropTarget = item;
        _dropTarget?.Classes.Add("dropTarget");
    }

    private static FileEntryViewModel? DirectoryAt(object? source) => EntryAt(source) is { IsDirectory: true } entry ? entry : null;

    private static ListBoxItem? ItemAt(object? source) => (source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);

    private static FileEntryViewModel? EntryAt(object? source) => ItemAt(source)?.DataContext as FileEntryViewModel;

    private sealed record PaneDrag(bool FromRemote, IReadOnlyList<FileEntryViewModel> Entries);
}
