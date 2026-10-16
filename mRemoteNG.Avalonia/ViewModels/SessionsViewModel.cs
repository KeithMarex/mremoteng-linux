using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Connection;

namespace mRemoteNG.Avalonia.ViewModels;

/// <summary>The session tabs.</summary>
public sealed partial class SessionsViewModel(SessionLauncher launcher) : ObservableObject
{
    public ObservableCollection<SessionViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSessions))]
    private SessionViewModel? _selected;

    public bool HasSessions => Items.Count > 0;

    /// <summary>The sessions that are connected; closing the app asks about them first.</summary>
    public int LiveCount => Items.Count(session => session.IsLive);

    /// <summary>Opens a tab for <paramref name="connection"/>; it connects as soon as its terminal is shown.</summary>
    public SessionViewModel Open(ConnectionInfo connection)
    {
        SessionViewModel session = new(connection, launcher);
        session.CloseRequested += OnCloseRequested;
        Items.Add(session);
        Selected = session;
        return session;
    }

    public void Close(SessionViewModel session)
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
        foreach (SessionViewModel session in Items.ToArray())
            Close(session);
    }

    partial void OnSelectedChanged(SessionViewModel? oldValue, SessionViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsSelected = false;
        if (newValue is not null)
            newValue.IsSelected = true;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close((SessionViewModel)sender!);
}
