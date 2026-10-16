using System.Text.Json;
using System.Text.Json.Serialization;
using mRemoteNG.App;
using mRemoteNG.Messages;

namespace mRemoteNG.Avalonia.Settings;

/// <summary>Loads and saves <see cref="AppSettings"/> as a JSON file readable only by the user.</summary>
public sealed class AppSettingsStore(string settingsFile)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string SettingsFile { get; } = settingsFile;

    /// <summary>Returns the stored settings, or the defaults when there are none or they cannot be read.</summary>
    public AppSettings Load()
    {
        if (!File.Exists(SettingsFile))
            return new AppSettings();

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            CoreRuntime.MessageCollector.AddExceptionMessage($"Could not read settings from {SettingsFile}; using defaults.",
                                                             ex, MessageClass.WarningMsg, logOnly: false);
            return new AppSettings();
        }
    }

    /// <summary>Writes the settings atomically. The file can hold default credentials, so only the user may read it.</summary>
    public void Save(AppSettings settings)
    {
        string directory = Path.GetDirectoryName(SettingsFile)!;
        Directory.CreateDirectory(directory);

        string temporaryFile = SettingsFile + ".tmp";
        File.WriteAllText(temporaryFile, JsonSerializer.Serialize(settings, JsonOptions));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temporaryFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporaryFile, SettingsFile, overwrite: true);
    }
}
