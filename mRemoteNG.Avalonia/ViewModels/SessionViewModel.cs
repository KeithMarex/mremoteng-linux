using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Messages;

namespace mRemoteNG.Avalonia.ViewModels;

public enum SessionState
{
    NotStarted,
    Running,
    Ended,
    Failed
}

/// <summary>
/// One session tab. It starts once its terminal is shown, and is listed in the connection's OpenConnections while it
/// runs, as the WinForms app does with its protocols. A session that ends cleanly closes its tab (like PuTTY's
/// default); otherwise, or when it was disconnected, the tab stays, with the reason and a Reconnect button.
/// </summary>
public sealed partial class SessionViewModel : ObservableObject, IProtocolSession, IDisposable
{
    private readonly SessionLauncher _launcher;
    private ITerminalHost? _host;
    private PreparedSession? _prepared;
    private bool _disconnecting;
    private bool _disposed;

    public SessionViewModel(ConnectionInfo connection, SessionLauncher launcher)
    {
        Connection = connection;
        _launcher = launcher;
        connection.PropertyChanged += OnConnectionPropertyChanged;
    }

    public ConnectionInfo Connection { get; }

    public string Title => Connection.Name;

    /// <summary>The connection's tab colour, when it has a valid one.</summary>
    public IBrush? TabBrush => Color.TryParse(Connection.TabColor, out Color color) ? new SolidColorBrush(color) : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive), nameof(HasEnded))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectCommand))]
    private SessionState _state;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    public bool IsLive => State == SessionState.Running;

    /// <summary>Whether the session is over (ended or failed to start), so it can be reconnected.</summary>
    public bool HasEnded => State is SessionState.Ended or SessionState.Failed;

    /// <summary>Raised when the tab should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Called by the view once its terminal can run a program; starts the session the first time.</summary>
    public async Task AttachAsync(ITerminalHost host)
    {
        if (_host is not null)
            _host.Exited -= OnExited;
        _host = host;
        host.Exited += OnExited;
        if (State == SessionState.NotStarted)
            await ConnectAsync();
    }

    /// <summary>Ends the session (IProtocolSession); the tab stays open.</summary>
    public void Disconnect()
    {
        if (!IsLive || _host is null)
            return;
        _disconnecting = true;
        _host.Kill();
    }

    [RelayCommand(CanExecute = nameof(HasEnded))]
    private Task ReconnectAsync() => ConnectAsync();

    [RelayCommand]
    private void Close()
    {
        Disconnect();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Disconnect();
        if (_host is not null)
            _host.Exited -= OnExited;
        Connection.PropertyChanged -= OnConnectionPropertyChanged;
        Connection.OpenConnections.Remove(this);
        _prepared?.Dispose();
    }

    private async Task ConnectAsync()
    {
        if (_host is null || IsLive || _disposed)
            return;

        _prepared?.Dispose();
        _prepared = null;
        PreparedSession prepared;
        try
        {
            prepared = _launcher.Prepare(Connection);
        }
        catch (SessionException ex)
        {
            Fail(ex.Message);
            return;
        }

        foreach (string warning in prepared.Warnings)
            CoreRuntime.MessageCollector.AddMessage(MessageClass.WarningMsg, $"{Title}: {warning}");
        CoreRuntime.MessageCollector.AddMessage(MessageClass.InformationMsg, UiText.ConnectingTo(Title, prepared.Plan.ToString()));
        try
        {
            await _host.StartAsync(prepared.Plan, prepared.Environment);
        }
        catch (Exception ex)
        {
            prepared.Dispose();
            Fail(ex.Message);
            return;
        }

        _prepared = prepared;
        State = SessionState.Running;
        StatusText = prepared.Plan.ToString();
        Connection.OpenConnections.Add(this);
    }

    private void OnExited(object? sender, int? exitCode)
    {
        if (!IsLive)
            return;

        // A killed program may still exit with 0, so a requested disconnect never counts as a clean end.
        bool disconnected = _disconnecting;
        _disconnecting = false;
        Connection.OpenConnections.Remove(this);
        _prepared?.Dispose();
        _prepared = null;
        State = SessionState.Ended;
        StatusText = disconnected ? UiText.Disconnected : UiText.SessionEnded(exitCode);
        CoreRuntime.MessageCollector.AddMessage(MessageClass.InformationMsg, $"{Title}: {StatusText}");
        if (exitCode == 0 && !disconnected)
            CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Fail(string reason)
    {
        State = SessionState.Failed;
        StatusText = UiText.CouldNotConnect(reason);
        CoreRuntime.MessageCollector.AddMessage(MessageClass.ErrorMsg, $"{Title}: {StatusText}");
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
