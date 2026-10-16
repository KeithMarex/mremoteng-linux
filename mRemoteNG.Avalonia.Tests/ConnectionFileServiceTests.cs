using System.Security;
using System.Xml.Linq;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Services;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Config.Settings;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Security;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class ConnectionFileServiceTests
{
    private TempDirectory _directory = null!;
    private AppSettings _settings = null!;
    private ConnectionFileService _service = null!;

    [SetUp]
    public void Setup()
    {
        _directory = new TempDirectory();
        _settings = new AppSettings();
        CoreRuntime.Settings = _settings.Core;
        CoreRuntime.IsConnectionsFileLoaded = false;
        _service = new ConnectionFileService(_settings);
    }

    [TearDown]
    public void Teardown()
    {
        _directory.Dispose();
        CoreRuntime.Settings = new CoreSettings();
    }

    [Test]
    public void NewFileIsWrittenInTheConfiguredVersion()
    {
        string path = _directory.File("confCons.xml");

        _service.CreateNew(path);

        Assert.That(XDocument.Load(path).Root?.Attribute("ConfVersion")?.Value, Is.EqualTo("2.7"));
        Assert.That(_service.RootNode, Is.Not.Null);
    }

    [Test]
    public void OpensAFileWrittenByWindowsMremoteng()
    {
        string path = _directory.File("confCons.xml");
        File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "confCons_v2_6.xml"), path);

        ConnectionTreeModel model = _service.Open(path, UnexpectedPasswordPrompt);

        Assert.That(model.GetRecursiveChildList().Select(node => node.Name), Does.Contain("Connection2.1").And.Contain("Folder2"));
    }

    [Test]
    public void ConnectionsAndPasswordsSurviveSaveAndOpen()
    {
        string path = _directory.File("confCons.xml");
        _service.CreateNew(path);
        _service.RootNode!.AddChild(new ConnectionInfo
        {
            Name = "web1", Hostname = "web1.example.org", Protocol = ProtocolType.SSH2, Username = "deploy", Password = "s3cret"
        });

        _service.Save();
        ConnectionInfo loaded = new ConnectionFileService(_settings).Open(path, UnexpectedPasswordPrompt).GetRecursiveChildList().Single();

        Assert.That((loaded.Hostname, loaded.Protocol, loaded.Username, loaded.Password),
                    Is.EqualTo(("web1.example.org", ProtocolType.SSH2, "deploy", "s3cret")));
    }

    [Test]
    public void ProtectedFileAsksForThePasswordUntilItIsRight()
    {
        string path = SaveProtectedFile("correct horse");
        Queue<string> answers = new(["wrong", "correct horse"]);

        ConnectionTreeModel model = new ConnectionFileService(_settings).Open(path, () => Password(answers.Dequeue()));

        Assert.That(answers, Is.Empty);
        Assert.That(model.GetRecursiveChildList().Single().Name, Is.EqualTo("db1"));
    }

    [Test]
    public void CancelledPasswordKeepsThePreviousFileOpenAndSaveable()
    {
        string first = _directory.File("first.xml");
        _service.CreateNew(first);
        string protectedFile = SaveProtectedFile("pw", new ConnectionFileService(_settings));
        CoreRuntime.IsConnectionsFileLoaded = true;

        Assert.Throws<InvalidDataException>(() => _service.Open(protectedFile, () => Optional<SecureString>.Empty));

        Assert.That(_service.FilePath, Is.EqualTo(first));
        Assert.DoesNotThrow(() => _service.Save());
    }

    [Test]
    public void SavingIsRefusedWhenTheFileDidNotLoadCorrectly()
    {
        _service.CreateNew(_directory.File("confCons.xml"));
        CoreRuntime.IsConnectionsFileLoaded = false;

        Assert.Throws<InvalidOperationException>(() => _service.Save());
    }

    [Test]
    public void SavingKeepsABackupAndLeavesNoTemporaryFile()
    {
        string path = _directory.File("confCons.xml");
        _service.CreateNew(path);

        _service.Save();

        Assert.That(Directory.GetFiles(_directory.Path, "confCons.xml.*.backup"), Has.Length.EqualTo(1));
        Assert.That(File.Exists(path + ".saving"), Is.False);
    }

    [Test]
    public void SaveAsSwitchesToTheNewFile()
    {
        _service.CreateNew(_directory.File("old.xml"));
        string newPath = _directory.File("new.xml");

        _service.SaveAs(newPath);

        Assert.That(_service.FilePath, Is.EqualTo(newPath));
        Assert.That(File.Exists(newPath), Is.True);
    }

    [Test]
    public void MissingFileIsReported()
    {
        Assert.Throws<FileNotFoundException>(() => _service.Open(_directory.File("nope.xml"), UnexpectedPasswordPrompt));
    }

    private string SaveProtectedFile(string password, ConnectionFileService? service = null)
    {
        service ??= _service;
        string path = _directory.File($"protected-{Guid.NewGuid():N}.xml");
        service.CreateNew(path);
        service.RootNode!.PasswordString = password;
        service.RootNode.AddChild(new ConnectionInfo { Name = "db1" });
        service.Save();
        return path;
    }

    private static Optional<SecureString> Password(string password) => new(password.ConvertToSecureString());

    private static Optional<SecureString> UnexpectedPasswordPrompt() =>
        throw new AssertionException("The file is not protected; no password should be asked.");
}
