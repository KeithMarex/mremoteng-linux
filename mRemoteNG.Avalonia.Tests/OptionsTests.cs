using System.Xml.Linq;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Platform;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Config.Settings;
using mRemoteNG.Security;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class OptionsTests
{
    private TempDirectory _directory = null!;
    private AppPaths _paths = null!;
    private AppSettings _settings = null!;
    private FakeDialogService _dialogs = null!;
    private MainWindowViewModel _viewModel = null!;

    [SetUp]
    public async Task Setup()
    {
        _directory = new TempDirectory();
        _paths = new AppPaths(_directory.File("config"), _directory.File("data"));
        _settings = new AppSettings();
        CoreRuntime.Settings = _settings.Core;
        _dialogs = new FakeDialogService();
        _viewModel = new MainWindowViewModel(_settings, new AppSettingsStore(_paths.SettingsFile), _paths, _dialogs, action => action());
        await _viewModel.InitializeAsync();
    }

    [TearDown]
    public void Teardown()
    {
        _directory.Dispose();
        CoreRuntime.Settings = new CoreSettings();
    }

    [Test]
    public void OptionsStartFromTheCurrentSettings()
    {
        _settings.ConnectionFileVersion = "2.8";
        _settings.EncryptionEngine = BlockCipherEngines.Serpent;

        OptionsViewModel options = new(_settings);

        Assert.That(options.FileVersion.Version, Is.EqualTo("2.8"));
        Assert.That(options.EncryptionEngine, Is.EqualTo(BlockCipherEngines.Serpent));
    }

    [Test]
    public void IterationsAreKeptWithinTheWindowsLimits()
    {
        OptionsViewModel options = new(_settings) { KeyDerivationIterations = 999_999 };

        options.ApplyTo(_settings);

        Assert.That(_settings.EncryptionKeyDerivationIterations, Is.EqualTo(50000));
    }

    [Test]
    public async Task ConfirmedOptionsAreSavedAppliedAndUsedForTheNextSave()
    {
        AppTheme? applied = null;
        _viewModel.ApplyTheme = theme => applied = theme;
        _dialogs.EditOptions = options =>
        {
            options.FileVersion = OptionsViewModel.FileVersions.Single(choice => choice.Version == "2.9");
            options.Theme = AppTheme.Dark;
            return true;
        };

        await _viewModel.OpenOptionsCommand.ExecuteAsync(null);
        _viewModel.AddConnectionCommand.Execute(null);

        Assert.That(applied, Is.EqualTo(AppTheme.Dark));
        Assert.That(new AppSettingsStore(_paths.SettingsFile).Load().ConnectionFileVersion, Is.EqualTo("2.9"));
        Assert.That(XDocument.Load(_paths.DefaultConnectionFile).Root?.Attribute("ConfVersion")?.Value, Is.EqualTo("2.9"));
    }

    [Test]
    public async Task CancelledOptionsChangeNothing()
    {
        _dialogs.EditOptions = options =>
        {
            options.SaveConnectionsAfterEveryEdit = false;
            return false;
        };

        await _viewModel.OpenOptionsCommand.ExecuteAsync(null);

        Assert.That(_settings.SaveConnectionsAfterEveryEdit, Is.True);
    }
}
