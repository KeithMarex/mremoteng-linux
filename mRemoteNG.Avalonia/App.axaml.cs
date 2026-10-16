using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Platform;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Avalonia.Views;

namespace mRemoteNG.Avalonia;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppPaths paths = AppPaths.FromEnvironment();
            MessageLogFile log = new(CoreRuntime.MessageCollector, paths.LogFile);
            desktop.Exit += (_, _) => log.Dispose();
            AppSettingsStore settingsStore = new(paths.SettingsFile);
            AppSettings settings = settingsStore.Load();
            CoreRuntime.Settings = settings.Core;

            MainWindow window = new();
            MainWindowViewModel viewModel = new(settings, settingsStore, paths, new DialogService(window),
                                                action => Dispatcher.UIThread.Post(action));
            viewModel.ApplyTheme = ApplyTheme;
            ApplyTheme(settings.Theme);
            window.DataContext = viewModel;
            window.Opened += async (_, _) => await viewModel.InitializeAsync();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyTheme(AppTheme theme)
    {
        RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}
