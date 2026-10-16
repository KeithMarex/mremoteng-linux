using Avalonia;

namespace TerminalSpike;

internal static class Program
{
    /// <summary>Command-line arguments, read by <see cref="MainWindow"/> to open sessions at startup.</summary>
    internal static string[] Args { get; private set; } = [];

    [STAThread]
    public static void Main(string[] args)
    {
        Args = args;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
