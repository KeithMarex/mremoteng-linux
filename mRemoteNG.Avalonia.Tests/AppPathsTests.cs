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
