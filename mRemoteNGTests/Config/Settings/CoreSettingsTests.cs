using System;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using mRemoteNG.App;
using mRemoteNG.Config.Settings;
using mRemoteNG.Config.Settings.Registry;
using NUnit.Framework;

namespace mRemoteNGTests.Config.Settings;

public class CoreSettingsTests
{
    [Test]
    public void ModuleInitializerBacksCoreSettingsAndPolicy()
    {
        Assert.That(CoreRuntime.Settings, Is.InstanceOf<SettingsBackedCoreSettings>());
        Assert.That(CoreRuntime.Policy, Is.InstanceOf<RegistryCorePolicy>());
    }

    // The settings classes ICoreSettings draws from.
    private static readonly Type[] SettingsClasses =
    [
        typeof(mRemoteNG.Properties.Settings),
        typeof(mRemoteNG.Properties.OptionsBackupPage),
    ];

    // CoreSettings is what the Linux app starts from, so it must keep the defaults of the .settings files.
    [TestCaseSource(nameof(CoreSettingNames))]
    public void CoreSettingsDefaultMatchesSettingsDesigner(string name)
    {
        PropertyInfo coreProperty = typeof(ICoreSettings).GetProperty(name);
        PropertyInfo settingsProperty = Array.Find(
            Array.ConvertAll(SettingsClasses, settingsClass => settingsClass.GetProperty(name)),
            property => property != null);
        Assert.That(settingsProperty, Is.Not.Null, $"No settings class has a setting named {name}");

        var defaultValue = settingsProperty.GetCustomAttribute<DefaultSettingValueAttribute>();
        Assert.That(defaultValue, Is.Not.Null, $"{name} has no default in Settings.settings");

        object expected = Convert.ChangeType(defaultValue.Value, coreProperty.PropertyType, CultureInfo.InvariantCulture);
        Assert.That(coreProperty.GetValue(new CoreSettings()), Is.EqualTo(expected));
    }

    private static string[] CoreSettingNames() =>
        Array.ConvertAll(typeof(ICoreSettings).GetProperties(), property => property.Name);
}
