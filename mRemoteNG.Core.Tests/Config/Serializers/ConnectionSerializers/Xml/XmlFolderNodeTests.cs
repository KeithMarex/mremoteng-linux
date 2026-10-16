using System;
using System.Linq;
using System.Xml.Linq;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Security;
using mRemoteNG.Security.Factories;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNG.Core.Tests.Config.Serializers.ConnectionSerializers.Xml
{
    public class XmlFolderNodeTests
    {
        [TestCase("2.7")]
        [TestCase("2.8")]
        [TestCase("2.9")]
        public void AFolderKeepsItsIdAndChildrenThroughSaveAndLoad(string version)
        {
            RootNodeInfo root = new(RootNodeType.Connection);
            ContainerInfo folder = new() { Name = "servers" };
            folder.SetPluginProperty("mRp.Example", "set by a plugin");
            folder.AddChild(new ConnectionInfo { Name = "web1" });
            root.AddChild(folder);

            string xml = Serialize(root, version);
            ContainerInfo loaded = (ContainerInfo)Deserialize(xml).RootNodes[0].Children.Single();

            Assert.That(loaded.ConstantID, Is.EqualTo(folder.ConstantID));
            Assert.That(loaded.Children.Select(child => child.Name), Is.EqualTo(new[] { "web1" }));
            // Released versions read a folder's PluginData element as a connection, so folders get none.
            Assert.That(XDocument.Parse(xml).Descendants("PluginData"), Is.Empty);
        }

        [Test]
        public void PluginDataOfAFolderIsNotReadAsAConnection()
        {
            // As folders were written before: every reader so far took the PluginData element for a connection.
            const string xml = """
                <?xml version="1.0" encoding="utf-8"?>
                <Connections Name="Connections" Export="false" EncryptionEngine="AES" BlockCipherMode="GCM" KdfIterations="1000" FullFileEncryption="false" Protected="PROTECTED" ConfVersion="2.8">
                  <Node Name="folder" Type="Container" Expanded="true" Id="d1c1c2a6-2a75-4b37-9a44-6c2d1c0e7c11">
                    <PluginData>{"mRp.AWS.Enabled":"True"}</PluginData>
                    <Node Name="inside" Type="Connection" Id="a5bb0f23-4f0e-4b46-9fd4-cf2a2d2c6d47" Protocol="SSH2" />
                  </Node>
                </Connections>
                """;
            string isNotProtected = CryptoProvider().Encrypt("ThisIsNotProtected",
                                                             new RootNodeInfo(RootNodeType.Connection).PasswordString.ConvertToSecureString());

            ContainerInfo folder = (ContainerInfo)Deserialize(xml.Replace("PROTECTED", isNotProtected)).RootNodes[0].Children.Single();

            Assert.That(folder.Children.Select(child => child.Name), Is.EqualTo(new[] { "inside" }));
        }

        private static string Serialize(RootNodeInfo root, string version)
        {
            ConnectionTreeModel model = new();
            model.AddRootNode(root);
            return new XmlConnectionSerializerFactory().Build(CryptoProvider(), model, fileVersion: Version.Parse(version)).Serialize(root);
        }

        private static ConnectionTreeModel Deserialize(string xml) => new XmlConnectionsDeserializer().Deserialize(xml);

        private static ICryptographyProvider CryptoProvider() =>
            new CryptoProviderFactory(BlockCipherEngines.AES, BlockCipherModes.GCM).Build();
    }
}
