using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Avalonia.Views;
using mRemoteNG.Connection;

namespace mRemoteNG.Avalonia.ViewModels;

/// <summary>What every session tab has: its connection, title, tab colour, status, its SSH tunnel, and closing.</summary>
public abstract partial class SessionTabViewModel : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _closing = new();
    private TunnelLease? _tunnel;

    protected SessionTabViewModel(ConnectionInfo connection)
    {
        Connection = connection;
        connection.PropertyChanged += OnConnectionPropertyChanged;
    }

    public ConnectionInfo Connection { get; }

    public string Title => Connection.Name;

    /// <summary>The connection's icon, shown in its tab as on Windows.</summary>
    public Bitmap? Icon => AppImages.ConnectionIcon(Connection.Icon);

    /// <summary>The connection's tab colour, when it has a valid one.</summary>
    public IBrush? TabBrush => Color.TryParse(Connection.TabColor, out Color color) ? new SolidColorBrush(color) : null;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>Whether the session is connected; closing the app asks about live sessions first.</summary>
    public abstract bool IsLive { get; }

    /// <summary>Whether the session is over (ended or failed), so it can be reconnected; the tab is shown dimmed.</summary>
    public abstract bool HasEnded { get; }

    protected bool IsDisposed { get; private set; }

    /// <summary>Opens the SSH tunnels connections go through; set when the tab is added.</summary>
    internal SshTunnels? Tunnels { get; set; }

    /// <summary>Raised when the tab should close.</summary>
    public event EventHandler? CloseRequested;

    public void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        _closing.Cancel();
        EndSession();
        Connection.PropertyChanged -= OnConnectionPropertyChanged;
        OnDisposed();
        CloseTunnel();
    }

    /// <summary>
    /// What the session connects to: the connection itself or, when it goes through an SSH tunnel (its SSH Tunnel
    /// setting), a copy that points at the tunnel's local end. A tunnel opened for an earlier session is closed first.
    /// </summary>
    /// <exception cref="SessionException">The tunnel could not be opened, with the reason.</exception>
    /// <exception cref="OperationCanceledException">The tab was closed meanwhile.</exception>
    protected async Task<ConnectionInfo> OpenTunnelAsync(CancellationToken cancellation)
    {
        CloseTunnel();
        if (Tunnels is null || !SshTunnels.Uses(Connection))
            return Connection;

        StatusText = UiText.OpeningSshTunnelStatus(Connection.SSHTunnelConnectionName);
        using CancellationTokenSource opening = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _closing.Token);
        TunnelLease lease = await Tunnels.OpenAsync(Connection, opening.Token);
        if (IsDisposed)
        {
            lease.Dispose();
            throw new OperationCanceledException();
        }

        _tunnel = lease;
        StatusText = UiText.Connecting;
        return lease.Target;
    }

    /// <summary>Takes over a tunnel opened for the tab elsewhere (an external tool's), to close with it.</summary>
    internal void AdoptTunnel(TunnelLease lease)
    {
        CloseTunnel();
        _tunnel = lease;
    }

    /// <summary>Closes the session's SSH tunnel, once the session is over.</summary>
    protected void CloseTunnel()
    {
        _tunnel?.Dispose();
        _tunnel = null;
    }

    /// <summary>Ends the session, as closing its tab does.</summary>
    protected abstract void EndSession();

    protected virtual void OnDisposed()
    {
    }

    protected void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Close()
    {
        EndSession();
        RequestClose();
    }

    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, Connection))
            return;
        if (e.PropertyName == nameof(ConnectionInfo.Name))
            OnPropertyChanged(nameof(Title));
        else if (e.PropertyName == nameof(ConnectionInfo.TabColor))
            OnPropertyChanged(nameof(TabBrush));
        else if (e.PropertyName == nameof(ConnectionInfo.Icon))
            OnPropertyChanged(nameof(Icon));
    }
}
