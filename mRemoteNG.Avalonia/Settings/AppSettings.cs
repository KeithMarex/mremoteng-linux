using mRemoteNG.Config.Settings;
using mRemoteNG.Security;

namespace mRemoteNG.Avalonia.Settings;

/// <summary>The Linux app's settings, stored as JSON (see <see cref="AppSettingsStore"/>).</summary>
public sealed class AppSettings
{
    /// <summary>The connection file opened at startup; null means the default file in the data directory.</summary>
    public string? ConnectionFile { get; set; }

    /// <summary>
    /// The ConfVersion written when saving. 2.7 keeps files readable (and writable) by mRemoteNG 1.77.x on
    /// Windows; newer versions store more settings but need a newer Windows release.
    /// </summary>
    public string ConnectionFileVersion { get; set; } = "2.7";

    /// <summary>Save after every change, as the WinForms app does by default.</summary>
    public bool SaveConnectionsAfterEveryEdit { get; set; } = true;

    public AppTheme Theme { get; set; } = AppTheme.System;

    // Same defaults as the WinForms app's security options.
    public BlockCipherEngines EncryptionEngine { get; set; } = BlockCipherEngines.AES;
    public BlockCipherModes EncryptionBlockCipherMode { get; set; } = BlockCipherModes.GCM;
    public int EncryptionKeyDerivationIterations { get; set; } = 10000;
    public bool EncryptCompleteConnectionsFile { get; set; }

    /// <summary>The settings shared with Core (defaults for new connections and more).</summary>
    public CoreSettings Core { get; set; } = new();
}
