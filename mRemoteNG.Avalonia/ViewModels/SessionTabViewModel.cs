using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mRemoteNG.Connection;

namespace mRemoteNG.Avalonia.ViewModels;

/// <summary>What every session tab has: its connection, title, tab colour, status, and closing.</summary>
public abstract partial class SessionTabViewModel : ObservableObject, IDisposable
{
    protected SessionTabViewModel(ConnectionInfo connection)
    {
        Connection = connection;
        connection.PropertyChanged += OnConnectionPropertyChanged;
    }

    public ConnectionInfo Connection { get; }

    public string Title => Connection.Name;

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

    /// <summary>Raised when the tab should close.</summary>
    public event EventHandler? CloseRequested;

    public void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        EndSession();
        Connection.PropertyChanged -= OnConnectionPropertyChanged;
        OnDisposed();
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
    }
}
