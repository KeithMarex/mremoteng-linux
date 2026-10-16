using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Xml.Linq;
using mRemoteNG.Connection;
using mRemoteNG.Security;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Config.Serializers.ConnectionSerializers.Xml
{
    public class XmlConnectionSerializerFactory
    {
        /// <summary>The connection file versions this factory can write, oldest first.</summary>
        public static IReadOnlyList<Version> WritableVersions { get; } = [new Version(2, 7), new Version(2, 8), new Version(2, 9)];

        /// <param name="fileVersion">
        /// The ConfVersion to write; null writes the newest. An older version keeps the file readable by
        /// older mRemoteNG releases (1.77.x writes 2.7 and reads up to 2.8), at the cost of the settings
        /// that version cannot store.
        /// </param>
        public ISerializer<ConnectionInfo, string> Build(
            ICryptographyProvider cryptographyProvider,
            ConnectionTreeModel connectionTreeModel,
            SaveFilter? saveFilter = null,
            bool useFullEncryption = false,
            Version? fileVersion = null)
        {
            SecureString encryptionKey = connectionTreeModel
                .RootNodes.OfType<RootNodeInfo>()
                .First().PasswordString
                .ConvertToSecureString();

            ISerializer<ConnectionInfo, XElement> connectionNodeSerializer = BuildNodeSerializer(
                fileVersion,
                cryptographyProvider,
                encryptionKey,
                saveFilter ?? new SaveFilter());

            return new XmlConnectionsSerializer(cryptographyProvider, connectionNodeSerializer)
            {
                UseFullEncryption = useFullEncryption
            };
        }

        private static ISerializer<ConnectionInfo, XElement> BuildNodeSerializer(
            Version? fileVersion,
            ICryptographyProvider cryptographyProvider,
            SecureString encryptionKey,
            SaveFilter saveFilter)
        {
            return fileVersion?.ToString(2) switch
            {
                null or "2.9" => new XmlConnectionNodeSerializer29(cryptographyProvider, encryptionKey, saveFilter),
                "2.8" => new XmlConnectionNodeSerializer28(cryptographyProvider, encryptionKey, saveFilter),
                "2.7" => new XmlConnectionNodeSerializer27(cryptographyProvider, encryptionKey, saveFilter),
                _ => throw new ArgumentOutOfRangeException(nameof(fileVersion), fileVersion,
                                                           "mRemoteNG cannot write this connection file version.")
            };
        }
    }
}
