using System.IO;
using System.Linq;
using System.Xml;
using mRemoteNG.ExternalTools;
using NUnit.Framework;

namespace mRemoteNG.Core.Tests.ExternalTools
{
    public class ExternalToolsFileTests
    {
        // As Windows mRemoteNG writes it (ExternalAppsSaver), with an attribute from some other version added.
        private const string WindowsFile = """
            <?xml version="1.0" encoding="utf-8"?>
            <Apps>
                <App DisplayName="Traceroute" Category="Network" FileName="cmd" Arguments="/K tracert %HOSTNAME%" WorkingDir="" WaitForExit="False" TryToIntegrate="True" RunElevated="False" ShowOnToolbar="True" Future="kept" />
                <App DisplayName="Old" FileName="notepad.exe" Arguments="" />
            </Apps>
            """;

        private string _path;

        [SetUp]
        public void Setup() => _path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), ExternalToolsFile.FileName);

        [TearDown]
        public void Teardown()
        {
            string directory = Path.GetDirectoryName(_path)!;
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }

        [Test]
        public void AWindowsFileIsRead()
        {
            Write(WindowsFile);

            ExternalToolDefinition[] tools = ExternalToolsFile.Load(_path).ToArray();

            Assert.That(tools.Select(tool => tool.DisplayName), Is.EqualTo(new[] { "Traceroute", "Old" }));
            Assert.That((tools[0].Category, tools[0].FileName, tools[0].Arguments), Is.EqualTo(("Network", "cmd", "/K tracert %HOSTNAME%")));
            Assert.That((tools[0].TryIntegrate, tools[0].WaitForExit, tools[0].RunElevated), Is.EqualTo((true, false, false)));
            // Older files lack the newer attributes; ShowOnToolbar then defaults to on, as on Windows.
            Assert.That((tools[1].Category, tools[1].ShowOnToolbar), Is.EqualTo(("", true)));
        }

        [Test]
        public void SavingAndLoadingKeepsEverything()
        {
            Write(WindowsFile);
            ExternalToolDefinition[] tools = ExternalToolsFile.Load(_path).ToArray();
            tools[1].WaitForExit = true;

            ExternalToolsFile.Save(_path, tools);
            ExternalToolDefinition[] again = ExternalToolsFile.Load(_path).ToArray();

            Assert.That(again[0].OtherAttributes["Future"], Is.EqualTo("kept"));
            Assert.That(again[1].WaitForExit, Is.True);
            Assert.That(File.ReadAllText(_path), Does.Contain("TryToIntegrate=\"True\""));
        }

        [Test]
        public void NoFileMeansNoTools()
        {
            Assert.That(ExternalToolsFile.Load(_path), Is.Empty);
        }

        [Test]
        public void AnotherKindOfFileIsRefused()
        {
            Write("<Connections />");

            Assert.Throws<XmlException>(() => ExternalToolsFile.Load(_path));
        }

        [Test]
        public void ACloneHasItsOwnAttributes()
        {
            ExternalToolDefinition tool = new() { DisplayName = "a" };
            tool.OtherAttributes["x"] = "1";

            ExternalToolDefinition clone = tool.Clone();
            clone.OtherAttributes["x"] = "2";
            clone.DisplayName = "b";

            Assert.That((tool.DisplayName, tool.OtherAttributes["x"]), Is.EqualTo(("a", "1")));
        }

        private void Write(string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, content);
        }
    }
}
