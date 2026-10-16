using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace mRemoteNG.FileTransfer.Sftp;

public enum HostKeyStatus
{
    /// <summary>known_hosts has this key for the host.</summary>
    Trusted,

    /// <summary>known_hosts has no key of this type for the host.</summary>
    Unknown,

    /// <summary>known_hosts has another key of this type for the host: possibly someone in between.</summary>
    Changed,

    /// <summary>The key is marked @revoked.</summary>
    Revoked
}

/// <param name="Conflict">For <see cref="HostKeyStatus.Changed"/>: where the known key is ("file:line").</param>
/// <param name="KnownKeyTypes">The key types known_hosts has for the host.</param>
public sealed record HostKeyCheck(HostKeyStatus Status, string? Conflict, IReadOnlyList<string> KnownKeyTypes);

/// <summary>
/// OpenSSH's known_hosts files, read and written the way ssh does, so SFTP trusts exactly the hosts the terminal
/// trusts: plain and hashed ("|1|salt|hash") host names, "[host]:port" for other ports than 22, wildcards and
/// negations, and @revoked keys. @cert-authority lines are skipped (host certificates are not supported).
/// </summary>
public sealed class KnownHosts
{
    private readonly List<Entry> _entries;

    private KnownHosts(List<Entry> entries) => _entries = entries;

    /// <summary>Reads the files that exist; unreadable lines are skipped, as ssh does.</summary>
    public static KnownHosts Load(IEnumerable<string> files)
    {
        List<Entry> entries = [];
        foreach (string file in files.Where(File.Exists))
        {
            int lineNumber = 0;
            foreach (string line in File.ReadLines(file))
            {
                lineNumber++;
                if (Parse(line, file, lineNumber) is { } entry)
                    entries.Add(entry);
            }
        }

        return new KnownHosts(entries);
    }

    /// <summary>How known_hosts names a host: "host" on port 22, otherwise "[host]:port" (lower case, like ssh).</summary>
    public static string HostName(string host, int port) =>
        port == 22 ? host.ToLowerInvariant() : $"[{host.ToLowerInvariant()}]:{port}";

    public HostKeyCheck Check(string host, int port, byte[] key)
    {
        string name = HostName(host, port);
        string keyType = KeyType(key);

        if (_entries.Any(entry => entry.Marker == "@revoked" && entry.Key.AsSpan().SequenceEqual(key)))
            return new HostKeyCheck(HostKeyStatus.Revoked, null, []);

        List<Entry> forHost = _entries.Where(entry => entry.Marker is null && Matches(entry.Hosts, name)).ToList();
        IReadOnlyList<string> knownTypes = forHost.Select(entry => entry.KeyType).Distinct().ToList();
        if (forHost.Any(entry => entry.Key.AsSpan().SequenceEqual(key)))
            return new HostKeyCheck(HostKeyStatus.Trusted, null, knownTypes);
        if (forHost.FirstOrDefault(entry => entry.KeyType == keyType) is { } conflicting)
            return new HostKeyCheck(HostKeyStatus.Changed, $"{conflicting.File}:{conflicting.Line}", knownTypes);
        return new HostKeyCheck(HostKeyStatus.Unknown, null, knownTypes);
    }

    /// <summary>The key types known_hosts has for a host, to prefer them when connecting (as ssh does).</summary>
    public IReadOnlyList<string> KeyTypesFor(string host, int port)
    {
        string name = HostName(host, port);
        return _entries.Where(entry => entry.Marker is null && Matches(entry.Hosts, name)).Select(entry => entry.KeyType).Distinct().ToList();
    }

    /// <summary>Adds a host key to a known_hosts file (created readable by the user only), hashed like ssh with HashKnownHosts.</summary>
    public static void Add(string file, string host, int port, byte[] key, bool hash)
    {
        string name = HostName(host, port);
        string hosts = hash ? Hashed(name) : name;
        string line = $"{hosts} {KeyType(key)} {Convert.ToBase64String(key)}\n";

        string? directory = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            if (OperatingSystem.IsWindows())
                Directory.CreateDirectory(directory);
            else
                Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        bool created = !File.Exists(file);
        bool needsNewLine = !created && new FileInfo(file).Length > 0 && LastByte(file) != '\n';
        File.AppendAllText(file, (needsNewLine ? "\n" : string.Empty) + line);
        if (created && !OperatingSystem.IsWindows())
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>The key type a public key blob starts with ("ssh-ed25519", "ssh-rsa", ...).</summary>
    public static string KeyType(byte[] key)
    {
        if (key.Length < 4)
            return string.Empty;
        int length = (int)BinaryPrimitives.ReadUInt32BigEndian(key);
        return length > 0 && length <= key.Length - 4 ? Encoding.ASCII.GetString(key, 4, length) : string.Empty;
    }

    /// <summary>The fingerprint ssh shows: "SHA256:" and the unpadded base64 of the key's SHA-256.</summary>
    public static string Fingerprint(byte[] key) => "SHA256:" + Convert.ToBase64String(SHA256.HashData(key)).TrimEnd('=');

    /// <summary>Whether a known_hosts host field matches <paramref name="name"/>.</summary>
    internal static bool Matches(string hosts, string name)
    {
        if (hosts.StartsWith("|1|", StringComparison.Ordinal))
            return HashedMatches(hosts, name);

        bool matched = false;
        foreach (string pattern in hosts.Split(','))
        {
            bool negated = pattern.StartsWith('!');
            if (!WildcardMatches(negated ? pattern[1..] : pattern, name))
                continue;
            if (negated)
                return false;
            matched = true;
        }

        return matched;
    }

    private static bool HashedMatches(string hosts, string name)
    {
        string[] parts = hosts.Split('|');
        if (parts.Length != 4)
            return false;
        try
        {
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            return CryptographicOperations.FixedTimeEquals(HMACSHA1.HashData(salt, Encoding.UTF8.GetBytes(name)), expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Hashed(string name)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(20);
        byte[] hash = HMACSHA1.HashData(salt, Encoding.UTF8.GetBytes(name));
        return $"|1|{Convert.ToBase64String(salt)}|{Convert.ToBase64String(hash)}";
    }

    // ssh's match_pattern: '*' is any run of characters, '?' any one character; case-insensitive.
    private static bool WildcardMatches(string pattern, string text)
    {
        pattern = pattern.ToLowerInvariant();
        int p = 0, t = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == text[t]))
            {
                p++;
                t++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = t;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
            p++;
        return p == pattern.Length;
    }

    private static Entry? Parse(string line, string file, int lineNumber)
    {
        string text = line.Trim();
        if (text.Length == 0 || text.StartsWith('#'))
            return null;

        string[] fields = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string? marker = null;
        if (fields.Length > 0 && fields[0].StartsWith('@'))
        {
            marker = fields[0];
            fields = fields[1..];
            if (marker is not "@revoked")
                return null; // @cert-authority: host certificates are not supported
        }

        if (fields.Length < 3)
            return null;
        try
        {
            return new Entry(file, lineNumber, marker, fields[0], fields[1], Convert.FromBase64String(fields[2]));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static int LastByte(string file)
    {
        using FileStream stream = File.OpenRead(file);
        stream.Seek(-1, SeekOrigin.End);
        return stream.ReadByte();
    }

    private sealed record Entry(string File, int Line, string? Marker, string Hosts, string KeyType, byte[] Key);
}
