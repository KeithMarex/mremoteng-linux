using System.IO;
using System.Linq;
using System.Security;
using mRemoteNG.Config.Import;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Security;
using mRemoteNG.Security.Factories;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using mRemoteNGTests.TestHelpers;
using NUnit.Framework;

namespace mRemoteNG.Core.Tests.Config.Import
{
    public class MRemoteNGXmlImporterTests
    {
        private string _file;

        [SetUp]
        public void Setup()
        {
            _file = FileTestHelpers.NewTempFilePath(".xml");
            File.WriteAllText(_file, ProtectedConnectionFile("correct horse"));
        }

        [TearDown]
        public void Teardown() => FileTestHelpers.DeleteDirectory(Path.GetDirectoryName(_file));

        [Test]
        public void AProtectedFileImportsWithThePasswordAskedFor()
        {
            ContainerInfo destination = new();
            int asked = 0;

            FileImporters.ForFile(_file, () => { asked++; return new Optional<SecureString>("correct horse".ConvertToSecureString()); })
                         .Import(_file, destination);

            Assert.That(asked, Is.EqualTo(1));
            Assert.That(((ContainerInfo)destination.Children.Single()).Children.Select(node => node.Name), Is.EqualTo(new[] { "db1" }));
        }

        [Test]
        public void AProtectedFileWithoutAPasswordFailsWithAClearError()
        {
            ContainerInfo destination = new();

            var ex = Assert.Throws<InvalidDataException>(() => new MRemoteNGXmlImporter().Import(_file, destination));

            Assert.That(ex.Message, Does.Contain("password was not given"));
            Assert.That(destination.Children, Is.Empty);
        }

        private static string ProtectedConnectionFile(string password)
        {
            ConnectionTreeModel model = new();
            RootNodeInfo root = new(RootNodeType.Connection) { PasswordString = password };
            root.AddChild(new ConnectionInfo { Name = "db1" });
            model.AddRootNode(root);
            ICryptographyProvider cryptoProvider = new CryptoProviderFactory(BlockCipherEngines.AES, BlockCipherModes.GCM).Build();
            return new XmlConnectionSerializerFactory().Build(cryptoProvider, model).Serialize(root);
        }
    }
}
