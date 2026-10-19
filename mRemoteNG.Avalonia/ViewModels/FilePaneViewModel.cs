using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.FileTransfer;

namespace mRemoteNG.Avalonia.ViewModels;

/// <summary>A file or directory in one of the two panes, or the ".." entry for the directory above.</summary>
public sealed class FileEntryViewModel(string name, string fullPath, bool isDirectory, long size, DateTime? modified, string permissions,
                                       RemoteEntry? remote = null, bool isParent = false)
{
    /// <summary>The ".." entry at the top of a listing, as in FileZilla: opening it or dropping on it goes one directory up.</summary>
    public static FileEntryViewModel ParentEntry(string parentPath) => new("..", parentPath, true, 0, null, string.Empty, isParent: true);

    public string Name { get; } = name;
    public string FullPath { get; } = fullPath;
    public bool IsDirectory { get; } = isDirectory;
    public long Size { get; } = size;
    public string Permissions { get; } = permissions;
    public DateTime? Modified { get; } = modified;

    /// <summary>Whether this is the ".." entry, which is never transferred, renamed, moved or deleted.</summary>
    public bool IsParent { get; } = isParent;

    /// <summary>The server's entry, in the remote pane.</summary>
    public RemoteEntry? Remote { get; } = remote;

    public string SizeText => IsDirectory ? string.Empty : FileSizes.Format(Size);
    // Compact and the same everywhere, so the column fits.
    public string ModifiedText => Modified?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
}

/// <summary>A column of a file pane: how wide it is (the user drags its edge) and whether the list is sorted by it.</summary>
public sealed partial class FileColumnViewModel : ObservableObject
{
    public const double MaxWidth = 2000;
    private double _width;

    public FileColumnViewModel(FileSortColumn key, string header, double width, Action<FileSortColumn> sortBy)
    {
        Key = key;
        Header = header;
        MinWidth = key == FileSortColumn.Name ? 60 : 40;
        Width = width;
        SortCommand = new RelayCommand(() => sortBy(key));
    }

    public FileSortColumn Key { get; }
    public string Header { get; }
    public double MinWidth { get; }

    /// <summary>The width in pixels, kept between <see cref="MinWidth"/> and <see cref="MaxWidth"/> (stored widths too).</summary>
    public double Width
    {
        get => _width;
        set => SetProperty(ref _width, double.IsNaN(value) ? MinWidth : Math.Clamp(value, MinWidth, MaxWidth));
    }

    /// <summary>"▲" or "▼" on the column the list is sorted by.</summary>
    [ObservableProperty]
    private string _sortMark = string.Empty;

    /// <summary>Clicking the column header: sorts by this column, or the other way round.</summary>
    public IRelayCommand SortCommand { get; }
}

/// <summary>One side of the file transfer tab: a directory and what is in it.</summary>
public abstract partial class FilePaneViewModel : ObservableObject
{
    private readonly FileListLayout _layout;
    private readonly Action _saveLayout;

    /// <param name="layout">The sort order and column widths to start with; <see cref="SaveLayout"/> writes the user's changes back.</param>
    /// <param name="saveLayout">Stores the settings that hold <paramref name="layout"/>.</param>
    protected FilePaneViewModel(FileListLayout layout, Action saveLayout)
    {
        _layout = layout;
        _saveLayout = saveLayout;
        NameColumn = new FileColumnViewModel(FileSortColumn.Name, UiText.ColumnName, layout.NameWidth, SortBy);
        SizeColumn = new FileColumnViewModel(FileSortColumn.Size, UiText.ColumnSize, layout.SizeWidth, SortBy);
        ModifiedColumn = new FileColumnViewModel(FileSortColumn.Modified, UiText.ColumnModified, layout.ModifiedWidth, SortBy);
        PermissionsColumn = new FileColumnViewModel(FileSortColumn.Permissions, UiText.ColumnPermissions, layout.PermissionsWidth, SortBy);
        Columns = [NameColumn, SizeColumn, ModifiedColumn, PermissionsColumn];
        SortColumn = Enum.IsDefined(layout.SortColumn) ? layout.SortColumn : FileSortColumn.Name;
        SortDescending = layout.SortDescending;
        ShowSortMarks();
    }

    [ObservableProperty]
    private string _currentPath = string.Empty;

    /// <summary>The path box, which the user can type a path into.</summary>
    [ObservableProperty]
    private string _pathText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public FileColumnViewModel NameColumn { get; }
    public FileColumnViewModel SizeColumn { get; }
    public FileColumnViewModel ModifiedColumn { get; }
    public FileColumnViewModel PermissionsColumn { get; }

    /// <summary>The columns from left to right, for the header.</summary>
    public IReadOnlyList<FileColumnViewModel> Columns { get; }

    /// <summary>The column the list is sorted by. ".." and the directories stay on top either way, as in FileZilla.</summary>
    public FileSortColumn SortColumn { get; private set; }

    public bool SortDescending { get; private set; }

    public ObservableCollection<FileEntryViewModel> Entries { get; } = [];

    public ObservableCollection<FileEntryViewModel> SelectedEntries { get; } = [];

    /// <summary>The selection without the ".." entry: what the commands work on.</summary>
    public IReadOnlyList<FileEntryViewModel> ChosenEntries => SelectedEntries.Where(entry => !entry.IsParent).ToList();

    /// <summary>Raised when a directory cannot be shown, with the message.</summary>
    public event EventHandler<string>? Failed;

    /// <summary>Shows <paramref name="path"/>; on failure the pane stays where it was. Returns whether it worked.</summary>
    public async Task<bool> NavigateAsync(string path)
    {
        IsBusy = true;
        try
        {
            IReadOnlyList<FileEntryViewModel> entries = await ListAsync(path);
            CurrentPath = path;
            PathText = path;
            List<FileEntryViewModel> shown = [.. entries];
            if (ParentOf(path) is var parent && parent != path)
                shown.Add(FileEntryViewModel.ParentEntry(parent));
            Show(shown);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            PathText = CurrentPath;
            Failed?.Invoke(this, UiText.CouldNotList(path, ex.Message));
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task RefreshAsync() => NavigateAsync(CurrentPath);

    /// <summary>Sorts by <paramref name="column"/>: ascending, or descending when the list is sorted by it ascending already.</summary>
    public void SortBy(FileSortColumn column)
    {
        SortDescending = column == SortColumn && !SortDescending;
        SortColumn = column;
        ShowSortMarks();

        // The selection stays, as in a file manager.
        List<FileEntryViewModel> selected = SelectedEntries.ToList();
        SelectedEntries.Clear();
        Show(Entries.ToList());
        foreach (FileEntryViewModel entry in selected)
            SelectedEntries.Add(entry);
        SaveLayout();
    }

    /// <summary>Stores the sort order and the column widths, which new tabs start with, also after a restart.</summary>
    public void SaveLayout()
    {
        _layout.SortColumn = SortColumn;
        _layout.SortDescending = SortDescending;
        _layout.NameWidth = NameColumn.Width;
        _layout.SizeWidth = SizeColumn.Width;
        _layout.ModifiedWidth = ModifiedColumn.Width;
        _layout.PermissionsWidth = PermissionsColumn.Width;
        _saveLayout();
    }

    /// <summary>
    /// Whether <paramref name="entry"/> can be moved into <paramref name="directory"/>: not the ".." entry, not where it
    /// is already, and a directory not into itself.
    /// </summary>
    public bool CanMoveInto(FileEntryViewModel entry, string directory) =>
        !entry.IsParent && entry.FullPath != directory && ParentOf(entry.FullPath) != directory &&
        !directory.StartsWith(entry.FullPath.TrimEnd('/') + "/", StringComparison.Ordinal);

    protected abstract Task<IReadOnlyList<FileEntryViewModel>> ListAsync(string path);

    private void Show(IEnumerable<FileEntryViewModel> entries)
    {
        List<FileEntryViewModel> sorted = entries.Order(new FileEntryOrder(SortColumn, SortDescending)).ToList();
        Entries.Clear();
        foreach (FileEntryViewModel entry in sorted)
            Entries.Add(entry);
    }

    private void ShowSortMarks()
    {
        foreach (FileColumnViewModel column in Columns)
            column.SortMark = column.Key != SortColumn ? string.Empty : SortDescending ? "▼" : "▲";
    }

    protected abstract string ParentOf(string path);

    [RelayCommand]
    private Task UpAsync() => NavigateAsync(ParentOf(CurrentPath));

    [RelayCommand]
    private Task ReloadAsync() => RefreshAsync();

    [RelayCommand]
    private Task GoToPathAsync() => string.IsNullOrWhiteSpace(PathText) ? Task.CompletedTask : NavigateAsync(PathText.Trim());

    /// <summary>Opens a directory (double-click or Enter); files are left to the transfer commands.</summary>
    [RelayCommand]
    private Task OpenAsync(FileEntryViewModel? entry) =>
        entry is { IsDirectory: true } ? NavigateAsync(entry.FullPath) : Task.CompletedTask;
}

/// <summary>This computer's files.</summary>
public sealed class LocalPaneViewModel(FileListLayout layout, Action saveLayout) : FilePaneViewModel(layout, saveLayout)
{
    public static string HomeDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    protected override Task<IReadOnlyList<FileEntryViewModel>> ListAsync(string path) => Task.Run<IReadOnlyList<FileEntryViewModel>>(() =>
        new DirectoryInfo(path).EnumerateFileSystemInfos()
                               .Select(entry => new FileEntryViewModel(entry.Name, entry.FullName, entry is DirectoryInfo,
                                                                       entry is FileInfo file ? file.Length : 0, entry.LastWriteTime, string.Empty))
                               .ToList());

    protected override string ParentOf(string path) => Path.GetDirectoryName(path.TrimEnd('/')) is { Length: > 0 } parent ? parent : "/";
}

/// <summary>The server's files. Operations take turns: an FTP connection does one thing at a time.</summary>
public sealed class RemotePaneViewModel(FileListLayout layout, Action saveLayout) : FilePaneViewModel(layout, saveLayout)
{
    private readonly SemaphoreSlim _turn = new(1, 1);
    private IRemoteFileSystem? _fileSystem;

    public void Attach(IRemoteFileSystem? fileSystem)
    {
        _fileSystem = fileSystem;
        if (fileSystem is null)
            Entries.Clear();
    }

    /// <summary>Runs an operation on the server, after any running one.</summary>
    public async Task<T> RunAsync<T>(Func<IRemoteFileSystem, Task<T>> operation)
    {
        IRemoteFileSystem fileSystem = _fileSystem ?? throw new InvalidOperationException(UiText.NotConnected);
        await _turn.WaitAsync();
        try
        {
            return await operation(fileSystem);
        }
        finally
        {
            _turn.Release();
        }
    }

    protected override Task<IReadOnlyList<FileEntryViewModel>> ListAsync(string path) => RunAsync<IReadOnlyList<FileEntryViewModel>>(async fileSystem =>
        (await fileSystem.ListAsync(path, CancellationToken.None))
        .Select(entry => new FileEntryViewModel(entry.Name, entry.FullPath, entry.IsDirectory, entry.Size, entry.Modified, entry.Permissions, entry))
        .ToList());

    protected override string ParentOf(string path) => RemotePath.Parent(path);
}

/// <summary>
/// A file pane's order: ".." first, then the directories, then the files, each by the chosen column; equal values by name.
/// </summary>
internal sealed class FileEntryOrder(FileSortColumn column, bool descending) : IComparer<FileEntryViewModel>
{
    private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

    public int Compare(FileEntryViewModel? x, FileEntryViewModel? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null || y is null)
            return x is null ? -1 : 1;
        if (x.IsParent != y.IsParent)
            return x.IsParent ? -1 : 1;
        if (x.IsDirectory != y.IsDirectory)
            return x.IsDirectory ? -1 : 1;

        int order = column switch
        {
            FileSortColumn.Size => SizeOf(x).CompareTo(SizeOf(y)),
            FileSortColumn.Modified => Nullable.Compare(x.Modified, y.Modified),
            FileSortColumn.Permissions => string.CompareOrdinal(x.Permissions, y.Permissions),
            _ => Names.Compare(x.Name, y.Name)
        };
        if (order != 0)
            return descending ? -order : order;
        return Names.Compare(x.Name, y.Name);
    }

    // A directory's size (SFTP servers report 4096, for instance) is not shown, so it does not count either.
    private static long SizeOf(FileEntryViewModel entry) => entry.IsDirectory ? 0 : entry.Size;
}

internal static class FileSizes
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : value.ToString(value < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " " + Units[unit];
    }
}
