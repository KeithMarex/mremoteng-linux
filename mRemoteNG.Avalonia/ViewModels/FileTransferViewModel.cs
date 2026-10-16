using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Services;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.FileTransfer;
using mRemoteNG.Messages;

namespace mRemoteNG.Avalonia.ViewModels;

public enum FileTransferState
{
    Connecting,
    Connected,
    Failed,
    Disconnected
}

/// <summary>
/// A file transfer tab (SFTP or FTP): this computer's files beside the server's, and a list of transfers, which run
/// over a connection of their own so browsing goes on meanwhile.
/// </summary>
public sealed partial class FileTransferViewModel : SessionTabViewModel, IProtocolSession
{
    private readonly Func<ConnectionInfo, CancellationToken, Task<FileTransferTarget>> _prepare;
    private readonly IDialogService _dialogs;
    private readonly IFileTransferPrompts _prompts;
    private readonly Action<Action> _dispatch;
    private readonly ConnectionMemory _memory = new();
    private CancellationTokenSource? _connecting;
    private IRemoteFileSystem? _fileSystem;
    private TransferQueue? _queue;

    /// <param name="prepare">Turns the connection into a server to connect to (<see cref="FileTransferConnector.PrepareAsync"/>).</param>
    /// <param name="dispatch">Queues an action on the UI thread.</param>
    public FileTransferViewModel(ConnectionInfo connection, Func<ConnectionInfo, CancellationToken, Task<FileTransferTarget>> prepare,
                                 IDialogService dialogs, IFileTransferPrompts prompts, Action<Action> dispatch) : base(connection)
    {
        _prepare = prepare;
        _dialogs = dialogs;
        _prompts = prompts;
        _dispatch = dispatch;
        Local.Failed += OnPaneFailed;
        Remote.Failed += OnPaneFailed;
        Local.SelectedEntries.CollectionChanged += (_, _) => UploadCommand.NotifyCanExecuteChanged();
        Remote.SelectedEntries.CollectionChanged += (_, _) => NotifyRemoteSelectionCommands();
    }

    public LocalPaneViewModel Local { get; } = new();

    public RemotePaneViewModel Remote { get; } = new();

    public ObservableCollection<TransferItemViewModel> Transfers { get; } = [];

    /// <summary>The server, for the remote pane's header.</summary>
    [ObservableProperty]
    private string _serverDescription = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive), nameof(HasEnded))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectCommand), nameof(UploadCommand), nameof(DownloadCommand), nameof(NewFolderCommand),
                                nameof(RenameCommand), nameof(DeleteCommand))]
    private FileTransferState _state;

    /// <summary>Plain FTP: the password and the files cross the network unencrypted.</summary>
    [ObservableProperty]
    private bool _isUnencrypted;

    public override bool IsLive => State == FileTransferState.Connected;

    public override bool HasEnded => State is FileTransferState.Failed or FileTransferState.Disconnected;

    /// <summary>Connects and shows the start directory (the connection's, or the user's home on the server).</summary>
    public async Task ConnectAsync()
    {
        if (State == FileTransferState.Connected || IsDisposed)
            return;

        State = FileTransferState.Connecting;
        StatusText = UiText.Connecting;
        if (Local.CurrentPath.Length == 0)
            await Local.NavigateAsync(LocalPaneViewModel.HomeDirectory);

        CancellationTokenSource connecting = _connecting = new CancellationTokenSource();
        try
        {
            FileTransferTarget target = await _prepare(Connection, connecting.Token);
            foreach (string warning in target.Warnings)
                CoreRuntime.MessageCollector.AddMessage(MessageClass.WarningMsg, $"{Title}: {warning}");

            // Connecting may ask questions, which wait for the UI thread: never connect on it.
            IRemoteFileSystem fileSystem = await Task.Run(() => target.Connect(_prompts, _memory, connecting.Token), connecting.Token);
            if (connecting.IsCancellationRequested)
            {
                await fileSystem.DisposeAsync();
                return;
            }

            _fileSystem = fileSystem;
            Remote.Attach(fileSystem);
            _queue = new TransferQueue(token => target.Connect(_prompts, _memory, token));
            _queue.Changed += OnTransferChanged;
            string home = await Remote.RunAsync(remote => remote.GetHomeDirectoryAsync(connecting.Token));
            if (target.StartDirectory is not { } start || !await Remote.NavigateAsync(start))
                await Remote.NavigateAsync(home);

            ServerDescription = fileSystem.Description;
            IsUnencrypted = !fileSystem.IsEncrypted;
            State = FileTransferState.Connected;
            StatusText = IsUnencrypted ? UiText.UnencryptedWarning : fileSystem.Description;
            Connection.OpenConnections.Add(this);
            CoreRuntime.MessageCollector.AddMessage(IsUnencrypted ? MessageClass.WarningMsg : MessageClass.InformationMsg,
                                                    $"{Title}: {UiText.ConnectedTo(fileSystem.Description)}" +
                                                    (IsUnencrypted ? " " + UiText.UnencryptedWarning : string.Empty));
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed && State == FileTransferState.Connecting)
                Fail(UiText.ConnectionCancelled);
        }
        catch (Exception ex)
        {
            Fail(UiText.CouldNotConnect(ex.Message));
        }
    }

    /// <summary>Ends the connection (IProtocolSession); the tab stays open with a Reconnect button.</summary>
    public void Disconnect()
    {
        _connecting?.Cancel();
        if (_queue is { } queue)
        {
            queue.Changed -= OnTransferChanged;
            _ = queue.DisposeAsync().AsTask();
            _queue = null;
        }

        if (_fileSystem is { } fileSystem)
        {
            _ = fileSystem.DisposeAsync().AsTask();
            _fileSystem = null;
        }

        Remote.Attach(null);
        Connection.OpenConnections.Remove(this);
        if (State is FileTransferState.Connected or FileTransferState.Connecting)
        {
            State = FileTransferState.Disconnected;
            StatusText = UiText.Disconnected;
        }
    }

    /// <summary>Uploads local files or directories (dropped from a file manager, for instance) to the remote directory.</summary>
    public async Task UploadAsync(IReadOnlyList<string> localPaths)
    {
        if (_queue is not { } queue || localPaths.Count == 0)
            return;

        HashSet<string> existing = Remote.Entries.Select(entry => entry.Name).ToHashSet();
        int conflicts = localPaths.Count(path => existing.Contains(Path.GetFileName(path.TrimEnd('/'))));
        bool overwrite = conflicts > 0 && await _dialogs.ConfirmAsync(UiText.Upload, UiText.ConfirmOverwrite(conflicts, Remote.CurrentPath));
        foreach (string path in localPaths)
            Transfers.Add(new TransferItemViewModel(queue.Upload(path, Remote.CurrentPath, overwrite)));
    }

    /// <summary>Downloads server files or directories to the local directory.</summary>
    public async Task DownloadAsync(IReadOnlyList<FileEntryViewModel> entries)
    {
        if (_queue is not { } queue || entries.Count == 0)
            return;

        string target = Local.CurrentPath;
        int conflicts = entries.Count(entry => File.Exists(Path.Combine(target, entry.Name)) || Directory.Exists(Path.Combine(target, entry.Name)));
        bool overwrite = conflicts > 0 && await _dialogs.ConfirmAsync(UiText.Download, UiText.ConfirmOverwrite(conflicts, target));
        foreach (FileEntryViewModel entry in entries.Where(entry => entry.Remote is not null))
            Transfers.Add(new TransferItemViewModel(queue.Download(entry.Remote!, target, overwrite)));
    }

    protected override void EndSession() => Disconnect();

    protected override void OnDisposed()
    {
        foreach (TransferItemViewModel transfer in Transfers)
            transfer.Item.Cancel();
    }

    private bool IsConnected => State == FileTransferState.Connected;

    private bool HasLocalSelection => IsConnected && Local.SelectedEntries.Count > 0;

    private bool HasRemoteSelection => IsConnected && Remote.SelectedEntries.Count > 0;

    private bool HasOneRemoteSelected => IsConnected && Remote.SelectedEntries.Count == 1;

    private bool CanReconnect => HasEnded;

    [RelayCommand(CanExecute = nameof(CanReconnect))]
    private Task ReconnectAsync() => ConnectAsync();

    [RelayCommand(CanExecute = nameof(HasLocalSelection))]
    private Task UploadAsync() => UploadAsync(Local.SelectedEntries.Select(entry => entry.FullPath).ToList());

    [RelayCommand(CanExecute = nameof(HasRemoteSelection))]
    private Task DownloadAsync() => DownloadAsync(Remote.SelectedEntries.ToList());

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task NewFolderAsync()
    {
        if (await AskNameAsync(UiText.NewFolder, UiText.NewFolderPrompt, string.Empty) is not { } name)
            return;
        await OnServerAsync(remote => remote.CreateDirectoryAsync(RemotePath.Combine(Remote.CurrentPath, name), CancellationToken.None));
    }

    [RelayCommand(CanExecute = nameof(HasOneRemoteSelected))]
    private async Task RenameAsync()
    {
        FileEntryViewModel entry = Remote.SelectedEntries[0];
        if (await AskNameAsync(UiText.Rename, UiText.RenamePrompt(entry.Name), entry.Name) is not { } name || name == entry.Name)
            return;
        await OnServerAsync(remote => remote.RenameAsync(entry.FullPath, RemotePath.Combine(Remote.CurrentPath, name), CancellationToken.None));
    }

    [RelayCommand(CanExecute = nameof(HasRemoteSelection))]
    private async Task DeleteAsync()
    {
        List<FileEntryViewModel> entries = Remote.SelectedEntries.ToList();
        string what = entries.Count == 1 ? entries[0].Name : UiText.ItemCount(entries.Count);
        if (!await _dialogs.ConfirmAsync(UiText.Delete, UiText.ConfirmDeleteOnServer(what)))
            return;
        await OnServerAsync(async remote =>
        {
            foreach (FileEntryViewModel entry in entries.Where(entry => entry.Remote is not null))
                await remote.DeleteAsync(entry.Remote!, CancellationToken.None);
        });
    }

    [RelayCommand]
    private void ClearFinished()
    {
        foreach (TransferItemViewModel transfer in Transfers.Where(transfer => transfer.IsFinished).ToList())
            Transfers.Remove(transfer);
    }

    // A server path segment: no '/', '.' or '..'.
    private async Task<string?> AskNameAsync(string title, string prompt, string initial)
    {
        string? name = (await _dialogs.AskTextAsync(title, prompt, initial))?.Trim();
        if (string.IsNullOrEmpty(name))
            return null;
        if (RemotePath.IsSafeName(name))
            return name;
        await _dialogs.ShowErrorAsync(title, UiText.InvalidFileName(name));
        return null;
    }

    // Runs a change on the server and shows the result; a failure is reported, the tab stays connected.
    private async Task OnServerAsync(Func<IRemoteFileSystem, Task> change)
    {
        try
        {
            await Remote.RunAsync(async remote =>
            {
                await change(remote);
                return true;
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CoreRuntime.MessageCollector.AddMessage(MessageClass.ErrorMsg, $"{Title}: {ex.Message}");
            await _dialogs.ShowErrorAsync(Title, ex.Message);
        }

        await Remote.RefreshAsync();
    }

    private void NotifyRemoteSelectionCommands()
    {
        DownloadCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    private void OnTransferChanged(object? sender, TransferItem item) => _dispatch(() =>
    {
        TransferItemViewModel? row = Transfers.FirstOrDefault(transfer => transfer.Item == item);
        row?.Refresh();
        if (item.State == TransferState.Failed)
            CoreRuntime.MessageCollector.AddMessage(MessageClass.ErrorMsg, $"{Title}: {item.Name}: {item.Error}");
        if (item.State != TransferState.Done)
            return;

        // Show the new files where the user is looking at them.
        FilePaneViewModel destination = item.Direction == TransferDirection.Download ? Local : Remote;
        if (destination.CurrentPath == item.DestinationDirectory)
            _ = destination.RefreshAsync();
    });

    private void OnPaneFailed(object? sender, string message)
    {
        CoreRuntime.MessageCollector.AddMessage(MessageClass.ErrorMsg, $"{Title}: {message}");
        StatusText = message;
    }

    private void Fail(string reason)
    {
        Disconnect();
        State = FileTransferState.Failed;
        StatusText = reason;
        CoreRuntime.MessageCollector.AddMessage(MessageClass.ErrorMsg, $"{Title}: {reason}");
    }

    /// <summary>Whether a connection opens as a file transfer tab: FTP and SFTP, and SSH for "Open SFTP".</summary>
    public static bool CanOpen(ConnectionInfo connection) =>
        connection.Protocol is ProtocolType.FTP or ProtocolType.SFTP or ProtocolType.SSH2;
}
