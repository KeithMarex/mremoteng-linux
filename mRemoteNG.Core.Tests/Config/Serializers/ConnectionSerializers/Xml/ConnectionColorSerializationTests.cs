using System.Linq;
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
    public class ConnectionColorSerializationTests
    {
        [Test]
        public void ColorAndColorInheritanceSurviveSaveAndLoad()
        {
            ConnectionTreeModel model = new();
            RootNodeInfo root = new(RootNodeType.Connection);
            ContainerInfo folder = new() { Name = "folder", Color = "#336699" };
            ConnectionInfo connection = new() { Name = "server1" };
            connection.Inheritance.Color = true;
            folder.AddChild(connection);
            root.AddChild(folder);
            model.AddRootNode(root);
            ICryptographyProvider cryptoProvider = new CryptoProviderFactory(BlockCipherEngines.AES, BlockCipherModes.GCM).Build();

            string xml = new XmlConnectionSerializerFactory().Build(cryptoProvider, model).Serialize(root);
            ConnectionTreeModel loaded = new XmlConnectionsDeserializer().Deserialize(xml);

            ConnectionInfo loadedFolder = loaded.GetRecursiveChildList().Single(node => node.Name == "folder");
            ConnectionInfo loadedConnection = loaded.GetRecursiveChildList().Single(node => node.Name == "server1");
            Assert.That(loadedFolder.Color, Is.EqualTo("#336699"));
            Assert.That(loadedConnection.Inheritance.Color, Is.True);
            Assert.That(loadedConnection.Color, Is.EqualTo("#336699"));
        }
    }
}
