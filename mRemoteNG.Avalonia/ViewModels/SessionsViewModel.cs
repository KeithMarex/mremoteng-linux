using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Connection;

namespace mRemoteNG.Avalonia.ViewModels;

/// <summary>The session tabs: terminals and file transfers.</summary>
/// <param name="launcher">Prepares terminal sessions.</param>
/// <param name="createFileTransfer">Creates a file transfer tab for a connection.</param>
public sealed partial class SessionsViewModel(SessionLauncher launcher, Func<ConnectionInfo, FileTransferViewModel> createFileTransfer)
    : ObservableObject
{
    public ObservableCollection<SessionTabViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSessions))]
    private SessionTabViewModel? _selected;

    public bool HasSessions => Items.Count > 0;

    /// <summary>The sessions that are connected; closing the app asks about them first.</summary>
    public int LiveCount => Items.Count(session => session.IsLive);

    /// <summary>Opens a terminal tab for <paramref name="connection"/>; it connects as soon as its terminal is shown.</summary>
    public SessionViewModel Open(ConnectionInfo connection) => Add(new SessionViewModel(connection, launcher));

    /// <summary>Opens a file transfer tab (FTP, SFTP, or SFTP to an SSH connection's host) and connects it.</summary>
    public FileTransferViewModel OpenFileTransfer(ConnectionInfo connection)
    {
        FileTransferViewModel fileTransfer = Add(createFileTransfer(connection));
        _ = fileTransfer.ConnectAsync();
        return fileTransfer;
    }

    public void Close(SessionTabViewModel session)
    {
        int index = Items.IndexOf(session);
        if (index < 0)
            return;

        session.CloseRequested -= OnCloseRequested;
        session.Dispose();
        Items.RemoveAt(index);
        if (Selected == session || Selected is null)
            Selected = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
        OnPropertyChanged(nameof(HasSessions));
    }

    /// <summary>Ends every session (the app is closing).</summary>
    public void CloseAll()
    {
        foreach (SessionTabViewModel session in Items.ToArray())
            Close(session);
    }

    partial void OnSelectedChanged(SessionTabViewModel? oldValue, SessionTabViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsSelected = false;
        if (newValue is not null)
            newValue.IsSelected = true;
    }

    private T Add<T>(T session) where T : SessionTabViewModel
    {
        session.CloseRequested += OnCloseRequested;
        Items.Add(session);
        Selected = session;
        return session;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close((SessionTabViewModel)sender!);
}
