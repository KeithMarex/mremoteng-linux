using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using mRemoteNG.Avalonia.ViewModels;

namespace mRemoteNG.Avalonia.Views;

/// <summary>
/// A file transfer tab. Dragging files from one pane to the other downloads or uploads them; files dragged in from a
/// file manager are uploaded. Dropped on a directory or on "..", they go there; within one pane that moves them, as in
/// FileZilla. Double-click (or Enter) opens a directory.
/// </summary>
public partial class FileTransferView : UserControl
{
    private static readonly DataFormat<PaneDrag> PaneFormat = DataFormat.CreateInProcessFormat<PaneDrag>("mremoteng-file-pane");
    private const double DragThreshold = 4;

    private PointerPressedEventArgs? _dragStart;
    private Point _dragStartPoint;
    private ListBoxItem? _dropTarget;

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
    }

    private FileTransferViewModel? ViewModel => DataContext as FileTransferViewModel;

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
