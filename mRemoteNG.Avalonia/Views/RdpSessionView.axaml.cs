using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using mRemoteNG.Avalonia.Sessions.Rdp;
using mRemoteNG.Avalonia.ViewModels;

namespace mRemoteNG.Avalonia.Views;

/// <summary>An RDP tab's content: the window xfreerdp shows the remote desktop in, and its status.</summary>
public partial class RdpSessionView : UserControl, IRdpView
{
    private RdpSessionViewModel? _session;

    public RdpSessionView()
    {
        InitializeComponent();
        // xfreerdp needs the window to embed in, and its size, before it starts. Without one (not on X11) the
        // session opens in a window of its own once the tab is laid out.
        Host.WindowCreated += (_, _) => Dispatcher.UIThread.Post(Attach, DispatcherPriority.Background);
        Loaded += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (Host.WindowId is null)
                Attach();
        }, DispatcherPriority.ApplicationIdle);
    }

    public RdpDisplay Describe()
    {
        PixelSize size = Host.PixelSize;
        PixelSize screen = Host.ScreenSize;
        return new RdpDisplay(Host.WindowId, size.Width, size.Height, screen.Width, screen.Height);
    }

    public void FocusSession() => Host.FocusEmbedded();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_session is not null)
            _session.PropertyChanged -= OnSessionPropertyChanged;
        _session = DataContext as RdpSessionViewModel;
        if (_session is not null)
            _session.PropertyChanged += OnSessionPropertyChanged;
    }

    private async void Attach()
    {
        if (_session is not null)
            await _session.AttachAsync(this);
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Once the tab is shown, so the embedded window can take the keyboard.
        if (e.PropertyName == nameof(RdpSessionViewModel.IsSelected) && _session?.IsSelected == true)
            Dispatcher.UIThread.Post(() => _session?.Focus(), DispatcherPriority.Background);
    }
}
