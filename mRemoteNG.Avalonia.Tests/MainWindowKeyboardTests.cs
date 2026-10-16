using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Iciclecreek.Terminal;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Platform;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Avalonia.Tests;
using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Avalonia.Views;
using mRemoteNG.Config.Settings;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Container;
using NUnit.Framework;

[assembly: AvaloniaTestApplication(typeof(HeadlessApp))]

namespace mRemoteNG.Avalonia.Tests;

/// <summary>The real app, without a display.</summary>
public static class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class MainWindowKeyboardTests
{
    private TempDirectory _directory = null!;

    [SetUp]
    public void Setup() => _directory = new TempDirectory();

    [TearDown]
    public void Teardown()
    {
        _directory.Dispose();
        CoreRuntime.Settings = new CoreSettings();
    }

    [AvaloniaTest]
    public void KeysTypedInATerminalGoToItsProgramWhileTheTreeKeepsTheMenuShortcuts()
    {
        AppPaths paths = new(_directory.File("config"), _directory.File("data"));
        AppSettings settings = new();
        CoreRuntime.Settings = settings.Core;
        // A local terminal running cat: it ends at Ctrl+D (end of input) with exit code 0.
        SessionLauncher launcher = new(new SessionPrograms { Shell = "/bin/cat" }, _directory.File("run"));
        MainWindow window = new() { Width = 1000, Height = 700 };
        MainWindowViewModel viewModel = new(settings, new AppSettingsStore(paths.SettingsFile), paths,
                                            new DialogService(window), action => Dispatcher.UIThread.Post(action), launcher);
        window.DataContext = viewModel;
        window.Show();
        Task initialized = viewModel.InitializeAsync();
        WaitFor(() => initialized.IsCompleted);

        ContainerInfo root = (ContainerInfo)viewModel.RootNodes[0].Connection;
        root.AddChild(new ConnectionInfo { Name = "local", Protocol = ProtocolType.Terminal, Hostname = "" });
        viewModel.SelectedNode = viewModel.RootNodes[0].Children.Single();
        viewModel.ConnectCommand.Execute(null);
        TerminalControl terminal = WaitFor(() => window.GetVisualDescendants().OfType<TerminalControl>().FirstOrDefault(t => t.IsLive));

        terminal.Focus();
        Press(window, PhysicalKey.Q, RawInputModifiers.Control); // XON to the terminal, not File > Exit
        Assert.That(window.OwnedWindows, Is.Empty, "Ctrl+Q in the terminal asked to close the app");
        Press(window, PhysicalKey.D, RawInputModifiers.Control); // end of input to cat, not Edit > Duplicate
        Assert.That(root.Children, Has.Count.EqualTo(1), "Ctrl+D in the terminal duplicated the connection");
        WaitFor(() => viewModel.Sessions.Items.Count == 0); // cat got it, ended cleanly, and its tab closed

        window.GetVisualDescendants().OfType<TreeViewItem>().Single(item => item.IsSelected).Focus();
        Press(window, PhysicalKey.D, RawInputModifiers.Control);

        Assert.That(root.Children, Has.Count.EqualTo(2), "Ctrl+D in the tree duplicates");
        window.Close();
    }

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static T WaitFor<T>(Func<T?> condition) where T : class
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (condition() is { } result)
                return result;
            Thread.Sleep(20);
        }

        throw new TimeoutException("The window did not get there in time.");
    }

    private static void WaitFor(Func<bool> condition) => WaitFor(() => condition() ? (object)true : null);
}
