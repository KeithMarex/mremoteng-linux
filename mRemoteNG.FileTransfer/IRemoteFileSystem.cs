namespace mRemoteNG.FileTransfer;

public enum RemoteEntryKind
{
    File,
    Directory,
    Other
}

/// <summary>A file or directory on the server, as a listing returns it.</summary>
/// <param name="Permissions">The permissions as "ls -l" shows them ("drwxr-xr-x"), or empty when the server does not say.</param>
public sealed record RemoteEntry(string Name, string FullPath, RemoteEntryKind Kind, long Size, DateTime? Modified, string Permissions)
{
    public bool IsDirectory => Kind == RemoteEntryKind.Directory;
}

/// <summary>
/// The files of one connected server (SFTP or FTP). One operation at a time: a transfer queue opens its own
/// connection so browsing does not wait for transfers.
/// </summary>
public interface IRemoteFileSystem : IAsyncDisposable
{
    /// <summary>What it is connected to, for the user ("sftp://user@host:port").</summary>
    string Description { get; }

    /// <summary>Whether passwords and data are encrypted on the way (false for plain FTP).</summary>
    bool IsEncrypted { get; }

    /// <summary>The directory the server starts the user in.</summary>
    Task<string> GetHomeDirectoryAsync(CancellationToken cancellationToken);

    /// <summary>The entries of a directory, without "." and "..".</summary>
    Task<IReadOnlyList<RemoteEntry>> ListAsync(string directory, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken);

    Task DownloadAsync(string path, Stream destination, IProgress<long> bytesTransferred, CancellationToken cancellationToken);

    /// <summary>Uploads to <paramref name="path"/>, replacing a file that is there.</summary>
    Task UploadAsync(Stream source, string path, IProgress<long> bytesTransferred, CancellationToken cancellationToken);

    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken);

    Task RenameAsync(string path, string newPath, CancellationToken cancellationToken);

    /// <summary>Deletes a file, or a directory with everything in it.</summary>
    Task DeleteAsync(RemoteEntry entry, CancellationToken cancellationToken);
}

/// <summary>Paths on the server, which always use '/'.</summary>
public static class RemotePath
{
    public static string Combine(string directory, string name) =>
        directory.EndsWith('/') ? directory + name : directory + "/" + name;

    public static string Parent(string path)
    {
        string trimmed = path.TrimEnd('/');
        int slash = trimmed.LastIndexOf('/');
        return slash <= 0 ? "/" : trimmed[..slash];
    }

    public static string FileName(string path)
    {
        string trimmed = path.TrimEnd('/');
        return trimmed[(trimmed.LastIndexOf('/') + 1)..];
    }

    /// <summary>
    /// Whether a name from a server can be used as one path segment. A server decides what its listing says, so
    /// a name such as "../.bashrc" must never decide where a download goes.
    /// </summary>
    public static bool IsSafeName(string name) =>
        name.Length > 0 && name is not ("." or "..") && !name.Contains('/') && !name.Contains('\0');

    /// <summary>The local path for <paramref name="name"/> inside <paramref name="directory"/>.</summary>
    /// <exception cref="InvalidDataException">The name would leave the directory.</exception>
    public static string LocalTarget(string directory, string name)
    {
        if (!IsSafeName(name))
            throw new InvalidDataException($"The server sent a file name that is not allowed: \"{Printable(name)}\".");

        string root = Path.GetFullPath(directory);
        string target = Path.GetFullPath(Path.Combine(root, name));
        if (Path.GetDirectoryName(target) != root.TrimEnd(Path.DirectorySeparatorChar) && Path.GetDirectoryName(target) != root)
            throw new InvalidDataException($"The server sent a file name that is not allowed: \"{Printable(name)}\".");
        return target;
    }

    private static string Printable(string name) =>
        new(name.Select(c => char.IsControl(c) ? '?' : c).Take(100).ToArray());
}
