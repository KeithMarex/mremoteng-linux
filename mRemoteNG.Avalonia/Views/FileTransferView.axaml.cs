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
/// file manager are uploaded. Double-click (or Enter) opens a directory.
/// </summary>
public partial class FileTransferView : UserControl
{
    private static readonly DataFormat<PaneDrag> PaneFormat = DataFormat.CreateInProcessFormat<PaneDrag>("mremoteng-file-pane");
    private const double DragThreshold = 4;

    private PointerPressedEventArgs? _dragStart;
    private Point _dragStartPoint;

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
        if (EntryAt(start.Source) is not { } pressed)
            return;

        // The selection, if the drag started on it; otherwise just the entry under the pointer.
        List<FileEntryViewModel> selected = list.SelectedItems?.OfType<FileEntryViewModel>().ToList() ?? [];
        IReadOnlyList<FileEntryViewModel> entries = selected.Contains(pressed) ? selected : [pressed];
        DataTransfer data = new();
        data.Add(DataTransferItem.Create(PaneFormat, new PaneDrag(list == RemoteList, entries)));
        await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Copy);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool toRemote = sender == RemoteList;
        e.DragEffects = e.DataTransfer.TryGetValue(PaneFormat) is { } drag
            ? drag.FromRemote != toRemote ? DragDropEffects.Copy : DragDropEffects.None
            : toRemote && e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { IsLive: true } viewModel)
            return;

        bool toRemote = sender == RemoteList;
        if (e.DataTransfer.TryGetValue(PaneFormat) is { } drag)
        {
            if (drag.FromRemote && !toRemote)
                await viewModel.DownloadAsync(drag.Entries);
            else if (!drag.FromRemote && toRemote)
                await viewModel.UploadAsync(drag.Entries.Select(entry => entry.FullPath).ToList());
        }
        else if (toRemote && e.DataTransfer.TryGetFiles() is { } files)
        {
            await viewModel.UploadAsync(files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList());
        }
    }

    private static FileEntryViewModel? EntryAt(object? source) =>
        ((source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true))?.DataContext as FileEntryViewModel;

    private sealed record PaneDrag(bool FromRemote, IReadOnlyList<FileEntryViewModel> Entries);
}
