using System;
using System.Collections.Generic;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Container;
using mRemoteNG.Plugins;

namespace mRemoteNG.Config.Serializers.ConnectionSerializers
{
    /// <summary>
    /// How FTP and SFTP connections (mRemoteNG for Linux) are stored so that Windows mRemoteNG can still read the file.
    /// Windows reads an unknown protocol as RDP and drops what it does not know when it saves. So the file says RAW
    /// (for FTP) or SSH2 (for SFTP), which Windows opens harmlessly, and the real protocol goes in the plugin data,
    /// which newer Windows versions keep, and in the sidecar file (<see cref="ConnectionFileSidecar"/>), which survives
    /// older ones. The real protocol only comes back while the stored one is still that stand-in: a protocol changed
    /// on Windows wins.
    /// </summary>
    public static class FileTransferCompatibility
    {
        /// <summary>Plugin properties of mRemoteNG for Linux start with this.</summary>
        public const string KeyPrefix = "mRx.";

        /// <summary>The plugin property holding the real protocol.</summary>
        public const string ProtocolKey = KeyPrefix + "Protocol";

        public static bool IsFileTransfer(ProtocolType protocol) => protocol is ProtocolType.FTP or ProtocolType.SFTP;

        /// <summary>The protocol written to the file: RAW for FTP, SSH2 for SFTP, any other protocol as it is.</summary>
        public static ProtocolType StandIn(ProtocolType protocol) => protocol switch
        {
            ProtocolType.FTP => ProtocolType.RAW,
            ProtocolType.SFTP => ProtocolType.SSH2,
            _ => protocol
        };

        /// <summary>
        /// The plugin data written for a node: its plugin properties, plus the real protocol of an FTP or SFTP
        /// connection. Folders get none: every released Windows version (and this one, before reading only Node
        /// elements) reads a folder's PluginData element as one more connection. A folder's protocol is kept by the
        /// sidecar file instead.
        /// </summary>
        public static string SerializedPluginData(ConnectionInfo node)
        {
            if (node is ContainerInfo)
                return string.Empty;

            Dictionary<string, string> data = new(node.PluginProperties);
            if (IsFileTransfer(node.Protocol))
                data[ProtocolKey] = node.Protocol.ToString();
            return PluginConnectionDataSerializer.Serialize(data);
        }

        /// <summary>After reading a node: turns the stand-in back into FTP or SFTP, as its plugin data says.</summary>
        public static void RestoreProtocol(ConnectionInfo node)
        {
            if (!node.TryGetPluginProperty(ProtocolKey, out string stored))
                return;

            // A storage detail: it is written again from the protocol on every save.
            node.SetPluginProperty(ProtocolKey, null);
            Restore(node, stored);
        }

        /// <summary>Sets the node's protocol to <paramref name="storedProtocol"/> if its protocol is still the stand-in.</summary>
        internal static bool Restore(ConnectionInfo node, string? storedProtocol)
        {
            if (!Enum.TryParse(storedProtocol, ignoreCase: false, out ProtocolType protocol) || !IsFileTransfer(protocol)
                || !Enum.IsDefined(protocol))
                return false;
            if (node.Protocol != StandIn(protocol))
                return false;

            node.Protocol = protocol;
            return true;
        }
    }
}
