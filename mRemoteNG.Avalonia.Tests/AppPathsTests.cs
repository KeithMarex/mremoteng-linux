using mRemoteNG.Avalonia.Platform;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class AppPathsTests
{
    [Test]
    public void UsesXdgDirectoriesWhenSet()
    {
        AppPaths paths = AppPaths.FromEnvironment(Environment(("HOME", "/home/u"), ("XDG_CONFIG_HOME", "/cfg"), ("XDG_DATA_HOME", "/data")));

        Assert.That(paths.SettingsFile, Is.EqualTo("/cfg/mRemoteNG/settings.json"));
        Assert.That(paths.DefaultConnectionFile, Is.EqualTo("/data/mRemoteNG/confCons.xml"));
    }

    [Test]
    public void FallsBackToTheXdgDefaultsUnderHome()
    {
        AppPaths paths = AppPaths.FromEnvironment(Environment(("HOME", "/home/u")));

        Assert.That(paths.ConfigDirectory, Is.EqualTo("/home/u/.config/mRemoteNG"));
        Assert.That(paths.DataDirectory, Is.EqualTo("/home/u/.local/share/mRemoteNG"));
    }

    [Test]
    public void IgnoresRelativeXdgDirectories()
    {
        AppPaths paths = AppPaths.FromEnvironment(Environment(("HOME", "/home/u"), ("XDG_CONFIG_HOME", "relative/cfg")));

        Assert.That(paths.ConfigDirectory, Is.EqualTo("/home/u/.config/mRemoteNG"));
    }

    private static Func<string, string?> Environment(params (string Name, string Value)[] variables) =>
        name => variables.FirstOrDefault(variable => variable.Name == name).Value;
}

public class MessageLogFileTests
{
    private TempDirectory _directory = null!;

    [SetUp]
    public void Setup() => _directory = new TempDirectory();

    [TearDown]
    public void Teardown() => _directory.Dispose();

    [Test]
    public void LogsMessagesIncludingLogOnlyOnesButNotDebug()
    {
        Messages.MessageCollector collector = new();
        string path = _directory.File("state/mRemoteNG.log");

        using (new MessageLogFile(collector, path))
        {
            collector.AddMessage(Messages.MessageClass.ErrorMsg, "save failed", onlyLog: false);
            collector.AddMessage(Messages.MessageClass.InformationMsg, "loaded", onlyLog: true);
            collector.AddMessage(Messages.MessageClass.DebugMsg, "noise");
        }

        string log = File.ReadAllText(path);
        Assert.That(log, Does.Contain("[ErrorMsg] save failed").And.Contain("[InformationMsg] loaded").And.Not.Contain("noise"));
    }

    [Test]
    public void ALargeLogIsRotatedAtStartup()
    {
        string path = _directory.File("mRemoteNG.log");
        File.WriteAllText(path, new string('x', 2000));

        using (new MessageLogFile(new Messages.MessageCollector(), path, maxBytes: 1000))
        {
        }

        Assert.That(File.Exists(path + ".1"), Is.True);
    }

    [Test]
    public void StateDirectoryFollowsXdgStateHome()
    {
        AppPaths paths = AppPaths.FromEnvironment(name => name switch { "HOME" => "/home/u", "XDG_STATE_HOME" => "/state", _ => null });

        Assert.That(paths.LogFile, Is.EqualTo("/state/mRemoteNG/mRemoteNG.log"));
    }
}
