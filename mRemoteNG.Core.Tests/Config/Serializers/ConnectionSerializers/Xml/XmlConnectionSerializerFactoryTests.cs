using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol.RDP;
using mRemoteNG.Security;
using mRemoteNG.Security.Factories;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNG.Core.Tests.Config.Serializers.ConnectionSerializers.Xml
{
    public class XmlConnectionSerializerFactoryTests
    {
        private static readonly string[] Versions = ["2.7", "2.8", "2.9"];

        private ConnectionTreeModel _model;
        private RootNodeInfo _root;
        private ICryptographyProvider _cryptoProvider;

        [SetUp]
        public void Setup()
        {
            _model = new ConnectionTreeModel();
            _root = new RootNodeInfo(RootNodeType.Connection);
            _root.AddChild(new ConnectionInfo
            {
                Name = "server1", Hostname = "server1.example.org", Username = "admin", Password = "s3cret!"
            });
            _model.AddRootNode(_root);
            _cryptoProvider = new CryptoProviderFactory(BlockCipherEngines.AES, BlockCipherModes.GCM).Build();
        }

        [Test]
        public void WritableVersionsAreTheTestedOnes()
        {
            Assert.That(XmlConnectionSerializerFactory.WritableVersions.Select(version => version.ToString(2)), Is.EqualTo(Versions));
        }

        [Test]
        public void WritesTheNewestVersionByDefault()
        {
            Assert.That(ConfVersionOf(Serialize(null)), Is.EqualTo("2.9"));
        }

        [TestCaseSource(nameof(Versions))]
        public void WritesTheRequestedVersion(string version)
        {
            Assert.That(ConfVersionOf(Serialize(Version.Parse(version))), Is.EqualTo(version));
        }

        [TestCaseSource(nameof(Versions))]
        public void EachVersionLoadsBackWithItsCredentials(string version)
        {
            ConnectionTreeModel loaded = new XmlConnectionsDeserializer().Deserialize(Serialize(Version.Parse(version)));

            ConnectionInfo connection = loaded.GetRecursiveChildList().Single();
            Assert.That(connection.Hostname, Is.EqualTo("server1.example.org"));
            Assert.That(connection.Username, Is.EqualTo("admin"));
            Assert.That(connection.Password, Is.EqualTo("s3cret!"));
        }

        [TestCase("2.7", RDPDiskDrives.None, RDPDiskDrives.None)]
        [TestCase("2.7", RDPDiskDrives.Local, RDPDiskDrives.Local)]
        [TestCase("2.7", RDPDiskDrives.All, RDPDiskDrives.Local)] // 2.7 only stores on/off
        [TestCase("2.8", RDPDiskDrives.All, RDPDiskDrives.All)]
        [TestCase("2.9", RDPDiskDrives.Custom, RDPDiskDrives.Custom)]
        public void RedirectDiskDrivesSurvivesEachVersion(string version, RDPDiskDrives saved, RDPDiskDrives expected)
        {
            _root.Children.Single().RedirectDiskDrives = saved;

            ConnectionTreeModel loaded = new XmlConnectionsDeserializer().Deserialize(Serialize(Version.Parse(version)));

            Assert.That(loaded.GetRecursiveChildList().Single().RedirectDiskDrives, Is.EqualTo(expected));
        }

        [TestCaseSource(nameof(Versions))]
        public void EachVersionMatchesItsSchema(string version)
        {
            StringBuilder errors = new();
            XmlReaderSettings settings = new()
            {
                ValidationType = ValidationType.Schema,
                ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings
            };
            settings.Schemas.Add("http://mremoteng.org", SchemaPath(version));
            settings.ValidationEventHandler += (_, args) => errors.AppendLine($"{args.Severity}: {args.Message}");

            using (XmlReader reader = XmlReader.Create(new StringReader(Serialize(Version.Parse(version))), settings))
            {
                while (reader.Read())
                {
                }
            }

            Assert.That(errors.ToString(), Is.Empty);
        }

        [Test]
        public void RejectsVersionsItCannotWrite()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Serialize(new Version(2, 6)));
        }

        private string Serialize(Version version)
        {
            return new XmlConnectionSerializerFactory().Build(_cryptoProvider, _model, fileVersion: version).Serialize(_root);
        }

        private static string ConfVersionOf(string xml) => XDocument.Parse(xml).Root?.Attribute("ConfVersion")?.Value;

        private static string SchemaPath(string version, [CallerFilePath] string sourceFilePath = "")
        {
            return Path.Combine(Path.GetDirectoryName(sourceFilePath), "..", "..", "..", "..", "..",
                                "mRemoteNG", "Schemas", $"mremoteng_confcons_v{version.Replace('.', '_')}.xsd");
        }
    }
}
