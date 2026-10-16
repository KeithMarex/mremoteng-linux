using System.Collections.Concurrent;
using System.Text;

namespace mRemoteNG.FileTransfer.Tests;

/// <summary>A server in memory: directories and files by path.</summary>
internal sealed class FakeRemoteFileSystem : IRemoteFileSystem
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new();
    private readonly ConcurrentDictionary<string, List<RemoteEntry>> _extraEntries = new();
    private readonly HashSet<string> _directories = ["/"];

    public bool FailDownloads { get; set; }

    public TaskCompletionSource? Gate { get; set; }

    public string Description => "fake://server";

    public bool IsEncrypted => true;

    public void AddFile(string path, string content)
    {
        AddDirectory(RemotePath.Parent(path));
        _files[path] = Encoding.UTF8.GetBytes(content);
    }

    public void AddDirectory(string path)
    {
        for (string directory = path; directory != "/"; directory = RemotePath.Parent(directory))
            _directories.Add(directory);
    }

    public void AddListingEntry(string directory, RemoteEntry entry) => _extraEntries.GetOrAdd(directory, _ => []).Add(entry);

    public RemoteEntry Entry(string path) => _directories.Contains(path)
        ? new RemoteEntry(RemotePath.FileName(path), path, RemoteEntryKind.Directory, 0, null, "drwxr-xr-x")
        : new RemoteEntry(RemotePath.FileName(path), path, RemoteEntryKind.File, _files[path].Length, null, "-rw-r--r--");

    public string Content(string path) => Encoding.UTF8.GetString(_files[path]);

    public Task<string> GetHomeDirectoryAsync(CancellationToken cancellationToken) => Task.FromResult("/");

    public Task<IReadOnlyList<RemoteEntry>> ListAsync(string directory, CancellationToken cancellationToken)
    {
        IEnumerable<string> children = _directories.Concat(_files.Keys)
                                                   .Where(path => path != "/" && RemotePath.Parent(path) == directory.TrimEnd('/') && path != directory);
        List<RemoteEntry> entries = children.Select(Entry).ToList();
        entries.AddRange(_extraEntries.GetValueOrDefault(directory) ?? []);
        return Task.FromResult<IReadOnlyList<RemoteEntry>>(entries);
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(_files.ContainsKey(path) || _directories.Contains(path));

    public async Task DownloadAsync(string path, Stream destination, IProgress<long> bytesTransferred, CancellationToken cancellationToken)
    {
        if (Gate is not null)
            await Gate.Task.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (FailDownloads)
        {
            await destination.WriteAsync("par"u8.ToArray(), cancellationToken);
            throw new IOException("connection lost");
        }

        byte[] data = _files[path];
        await destination.WriteAsync(data, cancellationToken);
        bytesTransferred.Report(data.Length);
    }

    public async Task UploadAsync(Stream source, string path, IProgress<long> bytesTransferred, CancellationToken cancellationToken)
    {
        MemoryStream copy = new();
        await source.CopyToAsync(copy, cancellationToken);
        _files[path] = copy.ToArray();
        bytesTransferred.Report(copy.Length);
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        _directories.Add(path);
        return Task.CompletedTask;
    }

    /// <summary>Moves a file, or a directory with everything in it; like OpenSSH's server, never over something that is there.</summary>
    public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken)
    {
        if (_files.ContainsKey(newPath) || _directories.Contains(newPath))
            throw new IOException($"{newPath} already exists.");
        if (_files.TryRemove(path, out byte[]? data))
        {
            _files[newPath] = data;
            return Task.CompletedTask;
        }

        if (!_directories.Remove(path))
            throw new FileNotFoundException($"{path} does not exist.");
        _directories.Add(newPath);
        foreach (string directory in _directories.Where(directory => directory.StartsWith(path + "/", StringComparison.Ordinal)).ToList())
        {
            _directories.Remove(directory);
            _directories.Add(newPath + directory[path.Length..]);
        }

        foreach (string file in _files.Keys.Where(file => file.StartsWith(path + "/", StringComparison.Ordinal)).ToList())
        {
            if (_files.TryRemove(file, out byte[]? content))
                _files[newPath + file[path.Length..]] = content;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(RemoteEntry entry, CancellationToken cancellationToken)
    {
        _files.TryRemove(entry.FullPath, out _);
        _directories.Remove(entry.FullPath);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
