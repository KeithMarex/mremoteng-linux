namespace mRemoteNG.Avalonia.Platform;

/// <summary>Where the Linux app keeps its files, following the XDG base directory specification.</summary>
public sealed class AppPaths(string configDirectory, string dataDirectory)
{
    private const string AppDirectoryName = "mRemoteNG";

    /// <summary>Settings: <c>$XDG_CONFIG_HOME/mRemoteNG</c>, by default <c>~/.config/mRemoteNG</c>.</summary>
    public string ConfigDirectory { get; } = configDirectory;

    /// <summary>Connection files and backups: <c>$XDG_DATA_HOME/mRemoteNG</c>, by default <c>~/.local/share/mRemoteNG</c>.</summary>
    public string DataDirectory { get; } = dataDirectory;

    public string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");

    public string DefaultConnectionFile => Path.Combine(DataDirectory, "confCons.xml");

    public static AppPaths FromEnvironment(Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        string home = AbsoluteOrNull(getEnvironmentVariable("HOME"))
                      ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string configHome = AbsoluteOrNull(getEnvironmentVariable("XDG_CONFIG_HOME")) ?? Path.Combine(home, ".config");
        string dataHome = AbsoluteOrNull(getEnvironmentVariable("XDG_DATA_HOME")) ?? Path.Combine(home, ".local", "share");
        return new AppPaths(Path.Combine(configHome, AppDirectoryName), Path.Combine(dataHome, AppDirectoryName));
    }

    // The XDG spec says relative paths in these variables are invalid and must be ignored.
    private static string? AbsoluteOrNull(string? path) =>
        string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) ? null : path;
}
