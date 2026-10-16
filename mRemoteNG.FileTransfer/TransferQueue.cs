using System.Threading.Channels;

namespace mRemoteNG.FileTransfer;

public enum TransferDirection
{
    Download,
    Upload
}

public enum TransferState
{
    Queued,
    Running,
    Done,
    Failed,
    Cancelled
}

/// <summary>One download or upload of a file or a whole directory. Its properties change on the queue's worker thread.</summary>
public sealed class TransferItem
{
    private readonly CancellationTokenSource _cancellation = new();

    internal TransferItem(TransferDirection direction, string name, string source, string destinationDirectory, bool isDirectory, bool overwrite,
                          RemoteEntry? remote)
    {
        Direction = direction;
        Name = name;
        Source = source;
        DestinationDirectory = destinationDirectory;
        IsDirectory = isDirectory;
        Overwrite = overwrite;
        Remote = remote;
    }

    public TransferDirection Direction { get; }
    public string Name { get; }
    public string Source { get; }
    public string DestinationDirectory { get; }
    public bool IsDirectory { get; }

    /// <summary>Whether files that are already there are replaced; otherwise they are skipped.</summary>
    public bool Overwrite { get; }

    public TransferState State { get; internal set; } = TransferState.Queued;
    public long BytesDone { get; internal set; }
    public long BytesTotal { get; internal set; }
    public int FilesSkipped { get; internal set; }

    /// <summary>The file being transferred, for directories.</summary>
    public string? CurrentFile { get; internal set; }

    public string? Error { get; internal set; }

    internal RemoteEntry? Remote { get; }

    internal CancellationToken CancellationToken => _cancellation.Token;

    public bool IsFinished => State is TransferState.Done or TransferState.Failed or TransferState.Cancelled;

    public void Cancel()
    {
        if (!IsFinished)
            _cancellation.Cancel();
    }
}

/// <summary>
/// Runs downloads and uploads one after the other over a connection of its own, so the tab can keep browsing.
/// A download is written to "name.part" and renamed when complete, so a failed or cancelled one leaves no
/// half-written file under the real name.
/// </summary>
public sealed class TransferQueue : IAsyncDisposable
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(150);

    private readonly Func<CancellationToken, Task<IRemoteFileSystem>> _connect;
    private readonly Channel<TransferItem> _items = Channel.CreateUnbounded<TransferItem>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private IRemoteFileSystem? _connection;

    /// <param name="connect">Opens the queue's connection, the first time it is needed and again after it failed.</param>
    public TransferQueue(Func<CancellationToken, Task<IRemoteFileSystem>> connect)
    {
        _connect = connect;
        _worker = Task.Run(RunAsync);
    }

    /// <summary>Raised (on the worker thread) when an item changes: its state, or its progress a few times a second.</summary>
    public event EventHandler<TransferItem>? Changed;

    public TransferItem Download(RemoteEntry entry, string localDirectory, bool overwrite) =>
        Enqueue(new TransferItem(TransferDirection.Download, entry.Name, entry.FullPath, localDirectory, entry.IsDirectory, overwrite, entry));

    public TransferItem Upload(string localPath, string remoteDirectory, bool overwrite) =>
        Enqueue(new TransferItem(TransferDirection.Upload, Path.GetFileName(localPath.TrimEnd(Path.DirectorySeparatorChar)), localPath,
                                 remoteDirectory, Directory.Exists(localPath), overwrite, null));

    public async ValueTask DisposeAsync()
    {
        _items.Writer.TryComplete();
        await _stop.CancelAsync();
        try
        {
            await _worker;
        }
        catch (OperationCanceledException)
        {
        }

        if (_connection is not null)
            await _connection.DisposeAsync();
    }

    private TransferItem Enqueue(TransferItem item)
    {
        _items.Writer.TryWrite(item);
        Changed?.Invoke(this, item);
        return item;
    }

    private async Task RunAsync()
    {
        await foreach (TransferItem item in _items.Reader.ReadAllAsync(_stop.Token))
        {
            if (item.CancellationToken.IsCancellationRequested)
            {
                Finish(item, TransferState.Cancelled);
                continue;
            }

            item.State = TransferState.Running;
            Changed?.Invoke(this, item);
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(item.CancellationToken, _stop.Token);
            try
            {
                _connection ??= await _connect(cancellation.Token);
                if (item.Direction == TransferDirection.Download)
                    await DownloadAsync(_connection, item, cancellation.Token);
                else
                    await UploadAsync(_connection, item, cancellation.Token);
                Finish(item, TransferState.Done);
            }
            catch (OperationCanceledException)
            {
                Finish(item, TransferState.Cancelled);
                await DropConnectionAsync();
            }
            catch (Exception ex)
            {
                item.Error = ex.Message;
                Finish(item, TransferState.Failed);
                if (ex is not InvalidDataException)
                    await DropConnectionAsync();
            }
        }
    }

    // An interrupted transfer can leave the connection in a state the next one should not inherit.
    private async Task DropConnectionAsync()
    {
        if (_connection is null)
            return;
        await _connection.DisposeAsync();
        _connection = null;
    }

    private void Finish(TransferItem item, TransferState state)
    {
        item.State = state;
        item.CurrentFile = null;
        Changed?.Invoke(this, item);
    }

    private async Task DownloadAsync(IRemoteFileSystem remote, TransferItem item, CancellationToken cancellationToken)
    {
        // Every file first, so the progress has a total.
        List<(RemoteEntry File, string Target)> files = [];
        if (item.IsDirectory)
            await CollectRemoteAsync(remote, item.Remote!, RemotePath.LocalTarget(item.DestinationDirectory, item.Name), files, cancellationToken);
        else
            files.Add((item.Remote!, RemotePath.LocalTarget(item.DestinationDirectory, item.Name)));
        item.BytesTotal = files.Sum(file => file.File.Size);

        foreach ((RemoteEntry file, string target) in files)
        {
            if (File.Exists(target) && !item.Overwrite)
            {
                Skipped(item, file.Size);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string partial = target + ".part";
            long before = item.BytesDone;
            item.CurrentFile = file.Name;
            try
            {
                await using (FileStream stream = new(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                    await remote.DownloadAsync(file.FullPath, stream, Progress(item, before), cancellationToken);
                File.Move(partial, target, overwrite: true);
            }
            catch
            {
                File.Delete(partial);
                throw;
            }

            item.BytesDone = before + file.Size;
        }
    }

    private static async Task CollectRemoteAsync(IRemoteFileSystem remote, RemoteEntry directory, string localDirectory,
                                                 List<(RemoteEntry, string)> files, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(localDirectory);
        foreach (RemoteEntry entry in await remote.ListAsync(directory.FullPath, cancellationToken))
        {
            string target = RemotePath.LocalTarget(localDirectory, entry.Name);
            if (entry.IsDirectory)
                await CollectRemoteAsync(remote, entry, target, files, cancellationToken);
            else if (entry.Kind == RemoteEntryKind.File)
                files.Add((entry, target));
        }
    }

    private async Task UploadAsync(IRemoteFileSystem remote, TransferItem item, CancellationToken cancellationToken)
    {
        string target = RemotePath.Combine(item.DestinationDirectory, item.Name);
        List<(FileInfo File, string Target)> files = [];
        if (item.IsDirectory)
            await CollectLocalAsync(remote, new DirectoryInfo(item.Source), target, files, cancellationToken);
        else
            files.Add((new FileInfo(item.Source), target));
        item.BytesTotal = files.Sum(file => file.File.Length);

        foreach ((FileInfo file, string remotePath) in files)
        {
            if (!item.Overwrite && await remote.ExistsAsync(remotePath, cancellationToken))
            {
                Skipped(item, file.Length);
                continue;
            }

            long before = item.BytesDone;
            item.CurrentFile = file.Name;
            try
            {
                await using FileStream stream = file.OpenRead();
                await remote.UploadAsync(stream, remotePath, Progress(item, before), cancellationToken);
            }
            catch
            {
                await DeletePartialUploadAsync(remote, file.Name, remotePath);
                throw;
            }

            item.BytesDone = before + file.Length;
        }
    }

    // Leaves no half-uploaded file under the real name, if the connection still allows it.
    private static async Task DeletePartialUploadAsync(IRemoteFileSystem remote, string name, string path)
    {
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            if (await remote.ExistsAsync(path, timeout.Token))
                await remote.DeleteAsync(new RemoteEntry(name, path, RemoteEntryKind.File, 0, null, string.Empty), timeout.Token);
        }
        catch (Exception)
        {
            // Best effort: the transfer already failed.
        }
    }

    // Creates the remote directories on the way. Symbolic links to directories are not followed (they could loop).
    private static async Task CollectLocalAsync(IRemoteFileSystem remote, DirectoryInfo directory, string remoteDirectory,
                                                List<(FileInfo, string)> files, CancellationToken cancellationToken)
    {
        if (!await remote.ExistsAsync(remoteDirectory, cancellationToken))
            await remote.CreateDirectoryAsync(remoteDirectory, cancellationToken);
        foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
        {
            string target = RemotePath.Combine(remoteDirectory, entry.Name);
            if (entry is DirectoryInfo subdirectory)
            {
                if (subdirectory.LinkTarget is null)
                    await CollectLocalAsync(remote, subdirectory, target, files, cancellationToken);
            }
            else if (entry is FileInfo file)
            {
                files.Add((file, target));
            }
        }
    }

    private void Skipped(TransferItem item, long size)
    {
        item.FilesSkipped++;
        item.BytesDone += size;
    }

    private IProgress<long> Progress(TransferItem item, long before)
    {
        DateTime lastReport = DateTime.MinValue;
        return new ImmediateProgress<long>(bytes =>
        {
            item.BytesDone = before + bytes;
            if (DateTime.UtcNow - lastReport < ProgressInterval)
                return;
            lastReport = DateTime.UtcNow;
            Changed?.Invoke(this, item);
        });
    }
}
