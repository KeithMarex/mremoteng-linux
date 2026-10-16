using System.Collections.Concurrent;

namespace mRemoteNG.FileTransfer;

/// <summary>
/// What the user answered while connecting a file transfer tab: a typed password, key passphrases, and host keys or
/// certificates trusted for this session only. The tab's transfer connection uses it, so it does not ask again.
/// </summary>
public sealed class ConnectionMemory
{
    /// <summary>A password the user typed (a stored one is in the connection's options).</summary>
    public string? Password { get; set; }

    internal ConcurrentDictionary<string, string> Passphrases { get; } = new();

    /// <summary>Host keys (base64) and certificate fingerprints trusted until the tab closes.</summary>
    internal ConcurrentDictionary<string, bool> TrustedOnce { get; } = new();
}

/// <summary>The user declined to trust the server (or the server's key or certificate could not be trusted).</summary>
public sealed class ServerNotTrustedException(string message) : Exception(message);

/// <summary>Reports progress on the calling thread, unlike Progress&lt;T&gt;, which posts to a synchronization context.</summary>
internal sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
