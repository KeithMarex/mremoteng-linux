using CommunityToolkit.Mvvm.ComponentModel;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Security;

namespace mRemoteNG.Avalonia.ViewModels;

public sealed record FileVersionChoice(string Version, string Description)
{
    public override string ToString() => Description;
}

/// <summary>The options window: an editable copy of the settings, written back only on OK.</summary>
public sealed partial class OptionsViewModel : ObservableObject
{
    public static IReadOnlyList<FileVersionChoice> FileVersions { get; } =
    [
        new("2.7", "2.7 – mRemoteNG 1.77 and later"),
        new("2.8", "2.8 – mRemoteNG 1.78.1 and later"),
        new("2.9", "2.9 – development builds only")
    ];

    public static IReadOnlyList<BlockCipherEngines> EncryptionEngines { get; } = Enum.GetValues<BlockCipherEngines>();

    public static IReadOnlyList<BlockCipherModes> BlockCipherModes { get; } = Enum.GetValues<BlockCipherModes>();

    public static IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    // Same limits as the WinForms security options.
    public const decimal MinimumKeyDerivationIterations = 1000;
    public const decimal MaximumKeyDerivationIterations = 50000;

    [ObservableProperty] private FileVersionChoice _fileVersion;
    [ObservableProperty] private bool _saveConnectionsAfterEveryEdit;
    [ObservableProperty] private BlockCipherEngines _encryptionEngine;
    [ObservableProperty] private BlockCipherModes _encryptionBlockCipherMode;
    [ObservableProperty] private decimal _keyDerivationIterations;
    [ObservableProperty] private bool _encryptCompleteConnectionsFile;
    [ObservableProperty] private AppTheme _theme;

    public OptionsViewModel(AppSettings settings)
    {
        _fileVersion = FileVersions.FirstOrDefault(choice => choice.Version == settings.ConnectionFileVersion) ?? FileVersions[0];
        _saveConnectionsAfterEveryEdit = settings.SaveConnectionsAfterEveryEdit;
        _encryptionEngine = settings.EncryptionEngine;
        _encryptionBlockCipherMode = settings.EncryptionBlockCipherMode;
        _keyDerivationIterations = settings.EncryptionKeyDerivationIterations;
        _encryptCompleteConnectionsFile = settings.EncryptCompleteConnectionsFile;
        _theme = settings.Theme;
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.ConnectionFileVersion = FileVersion.Version;
        settings.SaveConnectionsAfterEveryEdit = SaveConnectionsAfterEveryEdit;
        settings.EncryptionEngine = EncryptionEngine;
        settings.EncryptionBlockCipherMode = EncryptionBlockCipherMode;
        settings.EncryptionKeyDerivationIterations =
            (int)Math.Clamp(KeyDerivationIterations, MinimumKeyDerivationIterations, MaximumKeyDerivationIterations);
        settings.EncryptCompleteConnectionsFile = EncryptCompleteConnectionsFile;
        settings.Theme = Theme;
    }
}
