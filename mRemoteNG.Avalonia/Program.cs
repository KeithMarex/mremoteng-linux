using System.Reflection;
using Avalonia;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Avalonia.Sessions.Askpass;

namespace mRemoteNG.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Started by a session rather than by the user: as ssh's askpass helper, or as a Raw or Rlogin session's relay.
        if (AskpassClient.IsRequested(Environment.GetEnvironmentVariable))
            return AskpassClient.Run(args, Environment.GetEnvironmentVariable);
        if (RawTcpRelay.IsRequested(args))
            return RawTcpRelay.Run(args);
        if (RloginRelay.IsRequested(args))
            return RloginRelay.Run(args);

        if (args.Contains("--version"))
        {
            Console.WriteLine($"mRemoteNG for Linux {Version}");
            return 0;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CoreRuntime.MessageCollector.AddExceptionStackTrace("Unhandled error", (Exception)e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            CoreRuntime.MessageCollector.AddExceptionStackTrace("Unobserved task error", e.Exception);

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static string Version =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "unknown";

    /// <summary>The X11 window class; desktop environments match it with the .desktop file (StartupWMClass).</summary>
    public const string WindowClass = "mremoteng";

    /// <summary>
    /// Whether Avalonia talks to an input method (IBus, Fcitx) over D-Bus: only when asked for with AVALONIA_IM_MODULE,
    /// otherwise Avalonia's own choice, which is to do so for Chinese, Japanese, Korean and Vietnamese locales.
    /// </summary>
    /// <remarks>
    /// Avalonia's default is to always do so, and with IBus that loses every character typed with a dead key, such as
    /// ' and " on US International: X11's own input method composes ' and space into ', which Avalonia then passes on
    /// to IBus as a key press without a key, and drops when IBus says it has handled it
    /// (https://github.com/AvaloniaUI/Avalonia/issues/18596). Without IBus, the composed character is typed.
    /// </remarks>
    private static bool? EnableInputMethod =>
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AVALONIA_IM_MODULE")) ? null : true;

    // Also used by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions { WmClass = WindowClass, EnableIme = EnableInputMethod })
            .LogToTrace();
}
