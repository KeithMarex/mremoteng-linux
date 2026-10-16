using System.Linq;
using mRemoteNG.App;
using mRemoteNG.Config.Serializers;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Connection;
using mRemoteNG.Security;
using mRemoteNG.Security.Factories;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNG.Core.Tests.Config.Serializers.ConnectionSerializers.Xml
{
    public class ConfVersionCompatibilityTests
    {
        [Test]
        public void FileWrittenByTheDefaultSerializerCanBeReadBack()
        {
            ConnectionTreeModel model = new();
            RootNodeInfo root = new(RootNodeType.Connection);
            root.AddChild(new ConnectionInfo { Name = "server1" });
            model.AddRootNode(root);
            ICryptographyProvider cryptoProvider = new CryptoProviderFactory(BlockCipherEngines.AES, BlockCipherModes.GCM).Build();

            string xml = new XmlConnectionSerializerFactory().Build(cryptoProvider, model).Serialize(root);
            ConnectionTreeModel loaded = new XmlConnectionsDeserializer().Deserialize(xml);

            Assert.That(loaded.GetRecursiveChildList().Select(node => node.Name), Is.EqualTo(new[] { "server1" }));
        }

        [Test]
        public void NewerFileVersionIsRejectedAndBlocksSavingOverIt()
        {
            CoreRuntime.IsConnectionsFileLoaded = true;
            const string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><Connections Name=\"Connections\" ConfVersion=\"9.9\" />";

            var ex = Assert.Throws<UnsupportedConnectionFileVersionException>(() => new XmlConnectionsDeserializer().Deserialize(xml));

            Assert.That(ex.FileVersion, Is.EqualTo(9.9));
            Assert.That(CoreRuntime.IsConnectionsFileLoaded, Is.False);
        }
    }
}
