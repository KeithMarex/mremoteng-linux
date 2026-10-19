using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Connection.Protocol.VNC;

namespace mRemoteNG.Avalonia.Views;

/// <summary>A VNC tab's content: the remote screen, Ctrl+Alt+Del and Refresh, and the status.</summary>
public partial class VncSessionView : UserControl
{
    private VncSessionViewModel? _session;

    public VncSessionView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_session is not null)
                await _session.AttachAsync();
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_session is not null)
            _session.PropertyChanged -= OnSessionPropertyChanged;
        _session = DataContext as VncSessionViewModel;
        if (_session is not null)
        {
            _session.PropertyChanged += OnSessionPropertyChanged;
            // Scroll bars only when the screen is shown pixel for pixel.
            ScrollBarVisibility scrolling = _session.SmartSize == SmartSizeMode.SmartSNo ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            Scroller.HorizontalScrollBarVisibility = scrolling;
            Scroller.VerticalScrollBarVisibility = scrolling;
        }
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Once the tab is shown, or the session is up, the screen takes the keyboard.
        if (e.PropertyName is nameof(VncSessionViewModel.IsSelected) or nameof(VncSessionViewModel.Client) &&
            _session is { IsSelected: true, Client: not null })
        {
            Dispatcher.UIThread.Post(() => Display.Focus(), DispatcherPriority.Background);
        }
    }
}
