using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Security;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class AppSettingsStoreTests
{
    private TempDirectory _directory = null!;

    [SetUp]
    public void Setup() => _directory = new TempDirectory();

    [TearDown]
    public void Teardown() => _directory.Dispose();

    [Test]
    public void MissingFileGivesDefaults()
    {
        AppSettings settings = new AppSettingsStore(_directory.File("settings.json")).Load();

        Assert.That(settings.ConnectionFileVersion, Is.EqualTo("2.7"));
        Assert.That(settings.EncryptionEngine, Is.EqualTo(BlockCipherEngines.AES));
        Assert.That(settings.Core.ConDefaultProtocol, Is.EqualTo("RDP"));
    }

    [Test]
    public void SavedSettingsLoadBack()
    {
        AppSettingsStore store = new(_directory.File("sub/settings.json"));
        AppSettings settings = new() { ConnectionFile = "/shared/confCons.xml", ConnectionFileVersion = "2.8" };
        settings.Core.ConDefaultProtocol = "SSH2";

        store.Save(settings);
        AppSettings loaded = store.Load();

        Assert.That(loaded.ConnectionFile, Is.EqualTo("/shared/confCons.xml"));
        Assert.That(loaded.ConnectionFileVersion, Is.EqualTo("2.8"));
        Assert.That(loaded.Core.ConDefaultProtocol, Is.EqualTo("SSH2"));
    }

    [Test, Platform(Exclude = "Win")]
    public void SettingsFileIsOnlyReadableByTheUser()
    {
        AppSettingsStore store = new(_directory.File("settings.json"));
        store.Save(new AppSettings());

        if (OperatingSystem.IsWindows())
            return;
        Assert.That(File.GetUnixFileMode(store.SettingsFile), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
    }

    [Test]
    public void CorruptFileGivesDefaults()
    {
        File.WriteAllText(_directory.File("settings.json"), "{ not json");

        AppSettings settings = new AppSettingsStore(_directory.File("settings.json")).Load();

        Assert.That(settings.ConnectionFile, Is.Null);
    }
}
