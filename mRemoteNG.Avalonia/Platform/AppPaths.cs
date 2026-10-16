namespace mRemoteNG.Avalonia.Platform;

/// <summary>Where the Linux app keeps its files, following the XDG base directory specification.</summary>
public sealed class AppPaths(string configDirectory, string dataDirectory, string? stateDirectory = null,
                             string? runtimeDirectory = null)
{
    private const string AppDirectoryName = "mRemoteNG";

    /// <summary>Settings: <c>$XDG_CONFIG_HOME/mRemoteNG</c>, by default <c>~/.config/mRemoteNG</c>.</summary>
    public string ConfigDirectory { get; } = configDirectory;

    /// <summary>Connection files and backups: <c>$XDG_DATA_HOME/mRemoteNG</c>, by default <c>~/.local/share/mRemoteNG</c>.</summary>
    public string DataDirectory { get; } = dataDirectory;

    /// <summary>Logs: <c>$XDG_STATE_HOME/mRemoteNG</c>, by default <c>~/.local/state/mRemoteNG</c>.</summary>
    public string StateDirectory { get; } = stateDirectory ?? dataDirectory;

    /// <summary>
    /// Sockets for the running app: <c>$XDG_RUNTIME_DIR/mRemoteNG</c> (a per-user tmpfs), or the state directory
    /// when the session has no runtime directory.
    /// </summary>
    public string RuntimeDirectory { get; } = runtimeDirectory ?? stateDirectory ?? dataDirectory;

    public string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");

    public string LogFile => Path.Combine(StateDirectory, "mRemoteNG.log");

    public string DefaultConnectionFile => Path.Combine(DataDirectory, "confCons.xml");

    public static AppPaths FromEnvironment(Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        string home = AbsoluteOrNull(getEnvironmentVariable("HOME"))
                      ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string configHome = AbsoluteOrNull(getEnvironmentVariable("XDG_CONFIG_HOME")) ?? Path.Combine(home, ".config");
        string dataHome = AbsoluteOrNull(getEnvironmentVariable("XDG_DATA_HOME")) ?? Path.Combine(home, ".local", "share");
        string stateHome = AbsoluteOrNull(getEnvironmentVariable("XDG_STATE_HOME")) ?? Path.Combine(home, ".local", "state");
        string? runtimeHome = AbsoluteOrNull(getEnvironmentVariable("XDG_RUNTIME_DIR"));
        return new AppPaths(Path.Combine(configHome, AppDirectoryName), Path.Combine(dataHome, AppDirectoryName),
                            Path.Combine(stateHome, AppDirectoryName),
                            runtimeHome is null ? null : Path.Combine(runtimeHome, AppDirectoryName));
    }

    // The XDG spec says relative paths in these variables are invalid and must be ignored.
    private static string? AbsoluteOrNull(string? path) =>
        string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) ? null : path;
}
