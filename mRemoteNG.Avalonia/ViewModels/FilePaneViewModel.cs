using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mRemoteNG.FileTransfer;

namespace mRemoteNG.Avalonia.ViewModels;

/// <summary>A file or directory in one of the two panes.</summary>
public sealed class FileEntryViewModel(string name, string fullPath, bool isDirectory, long size, DateTime? modified, string permissions,
                                       RemoteEntry? remote = null)
{
    public string Name { get; } = name;
    public string FullPath { get; } = fullPath;
    public bool IsDirectory { get; } = isDirectory;
    public long Size { get; } = size;
    public string Permissions { get; } = permissions;

    /// <summary>The server's entry, in the remote pane.</summary>
    public RemoteEntry? Remote { get; } = remote;

    public string SizeText => IsDirectory ? string.Empty : FileSizes.Format(Size);
    // Compact and the same everywhere, so the column fits.
    public string ModifiedText => modified?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
}

/// <summary>One side of the file transfer tab: a directory and what is in it.</summary>
public abstract partial class FilePaneViewModel : ObservableObject
{
    [ObservableProperty]
    private string _currentPath = string.Empty;

    /// <summary>The path box, which the user can type a path into.</summary>
    [ObservableProperty]
    private string _pathText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<FileEntryViewModel> Entries { get; } = [];

    public ObservableCollection<FileEntryViewModel> SelectedEntries { get; } = [];

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
            Entries.Clear();
            foreach (FileEntryViewModel entry in entries.OrderByDescending(entry => entry.IsDirectory)
                                                        .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Entries.Add(entry);
            }

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

    protected abstract Task<IReadOnlyList<FileEntryViewModel>> ListAsync(string path);

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
public sealed class LocalPaneViewModel : FilePaneViewModel
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
public sealed class RemotePaneViewModel : FilePaneViewModel
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
