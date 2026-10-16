using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using mRemoteNG.Connection;

namespace mRemoteNG.Config.Serializers.ConnectionSerializers
{
    /// <summary>
    /// The file next to a connection file ("confCons.xml.linux.json") that keeps what Windows mRemoteNG 1.77 and older
    /// drop when they save it: the real protocol of FTP and SFTP connections and their mRemoteNG for Linux ("mRx.")
    /// settings, by connection ID. It holds no passwords. A folder set to FTP or SFTP (for its children to inherit)
    /// is kept too, but only until Windows saves the file: Windows gives folders a new ID on every load.
    /// </summary>
    public static class ConnectionFileSidecar
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static string PathFor(string connectionFile) => connectionFile + ".linux.json";

        /// <summary>The sidecar for <paramref name="nodes"/>; null when none of them needs one.</summary>
        public static string? Build(IEnumerable<ConnectionInfo> nodes)
        {
            Dictionary<string, SidecarEntry?> entries = new();
            foreach (ConnectionInfo node in nodes.Where(node => FileTransferCompatibility.IsFileTransfer(node.Protocol)))
            {
                Dictionary<string, string> settings = node.PluginProperties
                                                          .Where(property => property.Key.StartsWith(FileTransferCompatibility.KeyPrefix, StringComparison.Ordinal))
                                                          .ToDictionary(property => property.Key, property => property.Value);
                entries[node.ConstantID] = new SidecarEntry(node.Protocol.ToString(), settings);
            }

            return entries.Count == 0
                ? null
                : JsonSerializer.Serialize(new SidecarFile(Comment, entries), JsonOptions);
        }

        /// <summary>
        /// Restores FTP and SFTP connections from a sidecar: the protocol where the file still has its stand-in, and
        /// then the settings the file no longer has. Returns how many connections were restored.
        /// </summary>
        /// <exception cref="JsonException">The sidecar is not valid.</exception>
        public static int Apply(string sidecar, IEnumerable<ConnectionInfo> nodes)
        {
            SidecarFile? file = JsonSerializer.Deserialize<SidecarFile>(sidecar);
            if (file?.Connections is not { } entries)
                return 0;

            int restored = 0;
            foreach (ConnectionInfo node in nodes)
            {
                if (!entries.TryGetValue(node.ConstantID, out SidecarEntry? entry) || entry is null)
                    continue;
                bool protocolRestored = FileTransferCompatibility.Restore(node, entry.Protocol);
                if (!protocolRestored && node.Protocol.ToString() != entry.Protocol)
                    continue; // changed on Windows: the entry no longer applies

                foreach ((string key, string value) in entry.Settings ?? [])
                {
                    if (key.StartsWith(FileTransferCompatibility.KeyPrefix, StringComparison.Ordinal) && !node.TryGetPluginProperty(key, out _))
                        node.SetPluginProperty(key, value);
                }

                if (protocolRestored)
                    restored++;
            }

            return restored;
        }

        private const string Comment =
            "Kept by mRemoteNG for Linux: FTP and SFTP connections, which the connection file stores as RAW and SSH2 for Windows mRemoteNG.";

        private sealed record SidecarFile(string? Comment, Dictionary<string, SidecarEntry?>? Connections);

        private sealed record SidecarEntry(string? Protocol, Dictionary<string, string>? Settings);
    }
}
