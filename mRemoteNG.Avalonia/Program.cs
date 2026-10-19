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

    // Also used by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions { WmClass = WindowClass })
            .LogToTrace();
}
