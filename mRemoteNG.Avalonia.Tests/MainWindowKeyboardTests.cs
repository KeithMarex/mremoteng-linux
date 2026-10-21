using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
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
        (MainWindow window, MainWindowViewModel viewModel, TerminalControl terminal) = OpenTerminal();
        ContainerInfo root = (ContainerInfo)viewModel.RootNodes[0].Connection;

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

    [AvaloniaTest]
    public void AKeyPressTheTerminalLeavesStaysUnhandledSoItsTextIsTyped()
    {
        (MainWindow window, MainWindowViewModel viewModel, TerminalControl terminal) = OpenTerminal();
        bool? handled = null;
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => handled = e.Handled, RoutingStrategies.Bubble, handledEventsToo: true);

        terminal.Focus();
        // How X11 hands over a character composed from a dead key (' then space): a key press without a key, which is
        // typed only when nothing handled it.
        window.KeyPress(Key.None, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(Key.None, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.That(handled, Is.False);

        Press(window, PhysicalKey.Q, RawInputModifiers.Control);
        Assert.That(handled, Is.True, "a chord in the terminal reached the window's shortcuts");
        EndAndClose(window, viewModel, terminal);
    }

    [AvaloniaTest]
    public void CtrlAndTheWheelZoomATerminal()
    {
        (MainWindow window, MainWindowViewModel viewModel, TerminalControl terminal) = OpenTerminal();
        Point middle = terminal.TranslatePoint(new Point(terminal.Bounds.Width / 2, terminal.Bounds.Height / 2), window)!.Value;
        double size = terminal.FontSize;

        Wheel(window, middle, 1, RawInputModifiers.Control);
        Assert.That(terminal.FontSize, Is.EqualTo(size + 1));
        Wheel(window, middle, -1, RawInputModifiers.Control);
        Wheel(window, middle, -1, RawInputModifiers.Control);
        Assert.That(terminal.FontSize, Is.EqualTo(size - 1));
        // A touchpad's half notches add up to a whole one.
        Wheel(window, middle, 0.5, RawInputModifiers.Control);
        Assert.That(terminal.FontSize, Is.EqualTo(size - 1));
        Wheel(window, middle, 0.5, RawInputModifiers.Control);
        Assert.That(terminal.FontSize, Is.EqualTo(size));

        Wheel(window, middle, 1, RawInputModifiers.None);
        Assert.That(terminal.FontSize, Is.EqualTo(size), "the wheel alone zoomed");

        for (int notch = 0; notch < 100; notch++)
            Wheel(window, middle, -1, RawInputModifiers.Control);
        Assert.That(terminal.FontSize, Is.EqualTo(SessionView.MinFontSize));
        for (int notch = 0; notch < 100; notch++)
            Wheel(window, middle, 1, RawInputModifiers.Control);
        Assert.That(terminal.FontSize, Is.EqualTo(SessionView.MaxFontSize));
        EndAndClose(window, viewModel, terminal);
    }

    // The main window with a local terminal running cat, which ends at Ctrl+D (end of input) with exit code 0.
    private (MainWindow Window, MainWindowViewModel ViewModel, TerminalControl Terminal) OpenTerminal()
    {
        // The terminal adds its look to the app once per process, and each test has an app of its own: without it the
        // terminal has no template and its program cannot start.
        Uri terminalLook = new("avares://Iciclecreek.Avalonia.Terminal/Themes/Generic.axaml");
        if (!Application.Current!.Styles.OfType<StyleInclude>().Any(include => include.Source == terminalLook))
            Application.Current.Styles.Add(new StyleInclude(terminalLook) { Source = terminalLook });

        AppPaths paths = new(_directory.File("config"), _directory.File("data"));
        AppSettings settings = new();
        CoreRuntime.Settings = settings.Core;
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
        return (window, viewModel, terminal);
    }

    // Ends cat first: a window with a live session asks before it closes.
    private static void EndAndClose(MainWindow window, MainWindowViewModel viewModel, TerminalControl terminal)
    {
        terminal.Focus();
        Press(window, PhysicalKey.D, RawInputModifiers.Control);
        WaitFor(() => viewModel.Sessions.Items.Count == 0);
        window.Close();
    }

    private static void Wheel(Window window, Point point, double notches, RawInputModifiers modifiers)
    {
        window.MouseWheel(point, new Vector(0, notches), modifiers);
        Dispatcher.UIThread.RunJobs();
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
