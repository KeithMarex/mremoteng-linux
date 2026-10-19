using System;
using System.Linq;
using System.Xml.Linq;
using mRemoteNG.Config.Serializers.ConnectionSerializers;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Csv;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Container;
using mRemoteNG.Security;
using mRemoteNG.Security.Factories;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNG.Core.Tests.Config.Serializers.ConnectionSerializers
{
    public class FileTransferCompatibilityTests
    {
        [TestCase("2.7")]
        [TestCase("2.8")]
        [TestCase("2.9")]
        public void FtpAndSftpAreStoredAsWhatWindowsKnowsAndComeBack(string version)
        {
            (RootNodeInfo root, ConnectionInfo ftp, ConnectionInfo sftp) = FileTransferTree();

            string xml = Serialize(root, version);

            XElement[] nodes = XDocument.Parse(xml).Descendants("Node").ToArray();
            Assert.That(nodes.Select(node => (string)node.Attribute("Protocol")), Is.EqualTo(new[] { "RAW", "SSH2" }));
            Assert.That(nodes.Select(node => (string)node.Element("PluginData")), Has.All.Contains("\"mRx.Protocol\""));

            ConnectionInfo[] loaded = Load(xml);
            Assert.That(loaded.Select(node => node.Protocol), Is.EqualTo(new[] { ProtocolType.FTP, ProtocolType.SFTP }));
            Assert.That(loaded[0].Port, Is.EqualTo(21));
            Assert.That(loaded[1].GetPluginProperty("mRx.RemotePath"), Is.EqualTo("/srv/www"));
            Assert.That(loaded.Select(node => node.TryGetPluginProperty(FileTransferCompatibility.ProtocolKey, out _)), Has.All.False,
                        "the marker is a storage detail");
        }

        [Test]
        public void AfterWindows177SavedTheFileTheSidecarRestoresFtpAndSftp()
        {
            (RootNodeInfo root, _, _) = FileTransferTree();
            string sidecar = ConnectionFileSidecar.Build(root.GetRecursiveChildList())!;
            string savedByWindows = WithoutPluginData(Serialize(root, "2.7"));

            ConnectionInfo[] withoutSidecar = Load(savedByWindows);
            Assert.That(withoutSidecar.Select(node => node.Protocol), Is.EqualTo(new[] { ProtocolType.RAW, ProtocolType.SSH2 }));

            ConnectionInfo[] loaded = Load(savedByWindows);
            Assert.That(ConnectionFileSidecar.Apply(sidecar, loaded), Is.EqualTo(2));
            Assert.That(loaded.Select(node => node.Protocol), Is.EqualTo(new[] { ProtocolType.FTP, ProtocolType.SFTP }));
            Assert.That(loaded[1].GetPluginProperty("mRx.RemotePath"), Is.EqualTo("/srv/www"));
            Assert.That(sidecar, Does.Not.Contain("secret"));
        }

        [Test]
        public void AProtocolChangedOnWindowsWinsOverTheSidecarAndThePluginData()
        {
            (RootNodeInfo root, _, _) = FileTransferTree();
            string sidecar = ConnectionFileSidecar.Build(root.GetRecursiveChildList())!;
            // Newer Windows versions keep the plugin data even when the user changes the protocol.
            string changedOnWindows = Serialize(root, "2.8").Replace("Protocol=\"RAW\"", "Protocol=\"Telnet\"");

            ConnectionInfo[] loaded = Load(changedOnWindows);
            ConnectionFileSidecar.Apply(sidecar, loaded);

            Assert.That(loaded.Select(node => node.Protocol), Is.EqualTo(new[] { ProtocolType.Telnet, ProtocolType.SFTP }));
        }

        [Test]
        public void AFolderKeepsItsSftpProtocolThroughTheSidecarWithoutPluginDataInTheFile()
        {
            RootNodeInfo root = new(RootNodeType.Connection);
            ContainerInfo folder = new() { Name = "servers", Protocol = ProtocolType.SFTP };
            folder.SetPluginProperty("mRp.Example", "kept by plugins");
            folder.AddChild(new ConnectionInfo { Name = "web1" });
            root.AddChild(folder);
            string sidecar = ConnectionFileSidecar.Build(root.GetRecursiveChildList())!;

            foreach (string version in new[] { "2.7", "2.8", "2.9" })
            {
                string xml = Serialize(root, version);
                Assert.That(XDocument.Parse(xml).Descendants("PluginData"), Is.Empty, version);

                ContainerInfo loaded = (ContainerInfo)Deserialize(xml).RootNodes[0].Children.Single();
                ConnectionFileSidecar.Apply(sidecar, loaded.GetRecursiveChildList().Prepend(loaded));
                Assert.That((loaded.Protocol, loaded.Children.Count), Is.EqualTo((ProtocolType.SFTP, 1)), version);
            }
        }

        [Test]
        public void ACsvExportWithTheMarkerImportsAsSftp()
        {
            const string csv = "Name;Id;Parent;NodeType;Protocol;Port;PluginData\n" +
                               "files;6a0f5a1c-7d3e-4d1f-8c38-1e1a9b2f0d11;;Connection;SSH2;22;{\"mRx.Protocol\":\"SFTP\"}";

            ConnectionInfo imported = new CsvConnectionsDeserializerMremotengFormat().Deserialize(csv).RootNodes[0].Children.Single();

            Assert.That(imported.Protocol, Is.EqualTo(ProtocolType.SFTP));
        }

        [Test]
        public void ANodeWithoutFileTransferNeedsNoSidecar()
        {
            RootNodeInfo root = new(RootNodeType.Connection);
            root.AddChild(new ConnectionInfo { Name = "ssh", Protocol = ProtocolType.SSH2 });

            Assert.That(ConnectionFileSidecar.Build(root.GetRecursiveChildList()), Is.Null);
        }

        [TestCase("2.7")]
        [TestCase("2.9")]
        public void SpiceIsStoredAsVncWithItsSettingsAndComesBack(string version)
        {
            RootNodeInfo root = new(RootNodeType.Connection);
            ConnectionInfo spice = new() { Name = "vm", Hostname = "pve.example", Protocol = ProtocolType.SPICE, Port = 8006 };
            spice.SetPluginProperty("mRx.ProxmoxVm", "105");
            root.AddChild(spice);

            string xml = Serialize(root, version);
            string sidecar = ConnectionFileSidecar.Build(root.GetRecursiveChildList())!;

            Assert.That((string)XDocument.Parse(xml).Descendants("Node").Single().Attribute("Protocol"), Is.EqualTo("VNC"));
            ConnectionInfo loaded = Load(xml).Single();
            Assert.That(loaded.Protocol, Is.EqualTo(ProtocolType.SPICE));
            Assert.That(loaded.GetPluginProperty("mRx.ProxmoxVm"), Is.EqualTo("105"));

            ConnectionInfo afterWindows177 = Load(WithoutPluginData(xml)).Single();
            Assert.That(afterWindows177.Protocol, Is.EqualTo(ProtocolType.VNC));
            Assert.That(ConnectionFileSidecar.Apply(sidecar, [afterWindows177]), Is.EqualTo(1));
            Assert.That(afterWindows177.Protocol, Is.EqualTo(ProtocolType.SPICE));
            Assert.That(afterWindows177.GetPluginProperty("mRx.ProxmoxVm"), Is.EqualTo("105"));
        }

        [TestCase(ProtocolType.FTP, 21)]
        [TestCase(ProtocolType.SFTP, 22)]
        [TestCase(ProtocolType.SPICE, 8006)]
        public void FileTransferHasTheUsualDefaultPorts(ProtocolType protocol, int port)
        {
            Assert.That(DefaultPorts.For(protocol), Is.EqualTo(port));
        }

        private static (RootNodeInfo Root, ConnectionInfo Ftp, ConnectionInfo Sftp) FileTransferTree()
        {
            RootNodeInfo root = new(RootNodeType.Connection);
            ConnectionInfo ftp = new() { Name = "ftp", Hostname = "files.example", Protocol = ProtocolType.FTP, Port = 21, Password = "secret" };
            ftp.SetPluginProperty("mRx.FtpEncryption", "Explicit");
            ConnectionInfo sftp = new() { Name = "sftp", Hostname = "web.example", Protocol = ProtocolType.SFTP, Port = 22 };
            sftp.SetPluginProperty("mRx.RemotePath", "/srv/www");
            root.AddChild(ftp);
            root.AddChild(sftp);
            return (root, ftp, sftp);
        }

        private static string Serialize(RootNodeInfo root, string version)
        {
            ConnectionTreeModel model = new();
            model.AddRootNode(root);
            return new XmlConnectionSerializerFactory().Build(CryptoProvider(), model, fileVersion: Version.Parse(version)).Serialize(root);
        }

        private static ConnectionTreeModel Deserialize(string xml) => new XmlConnectionsDeserializer().Deserialize(xml);

        private static ConnectionInfo[] Load(string xml) => Deserialize(xml).RootNodes[0].Children.ToArray();

        // Windows 1.77 drops elements it does not know when it saves.
        private static string WithoutPluginData(string xml)
        {
            XDocument document = XDocument.Parse(xml);
            document.Descendants("PluginData").Remove();
            return document.Declaration + Environment.NewLine + document;
        }

        private static ICryptographyProvider CryptoProvider() =>
            new CryptoProviderFactory(BlockCipherEngines.AES, BlockCipherModes.GCM).Build();
    }
}
