using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Platform;
using mRemoteNG.Avalonia.Services;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Config.Import;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Avalonia.ViewModels;

/// <summary>The main window: the connection tree, its properties, the sessions, notifications and the file commands.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly AppSettingsStore _settingsStore;
    private readonly AppPaths _paths;
    private readonly IDialogService _dialogs;
    private readonly Action<Action> _dispatch;
    private readonly ConnectionFileService _files;
    private ConnectionTreeModel? _model;
    private bool _savePending;
    private bool _changingFilePassword;

    /// <param name="dispatch">Queues an action on the UI thread (after the current one completes).</param>
    /// <param name="launcher">Prepares sessions; by default with ssh and telnet from the PATH.</param>
    public MainWindowViewModel(AppSettings settings, AppSettingsStore settingsStore, AppPaths paths,
                               IDialogService dialogs, Action<Action> dispatch, SessionLauncher? launcher = null)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _paths = paths;
        _dialogs = dialogs;
        _dispatch = dispatch;
        _files = new ConnectionFileService(settings);
        Messages = new MessagesViewModel(CoreRuntime.MessageCollector, dispatch);
        Sessions = CreateSessions(launcher ?? new SessionLauncher(new SessionPrograms(), paths.RuntimeDirectory));
        PropertyPanel.PropertyEdited += OnPropertyEdited;
        PropertyPanel.IsDescriptionVisible = settings.ShowPropertyDescription;
    }

    public ObservableCollection<ConnectionNodeViewModel> RootNodes { get; } = [];

    public PropertyPanelViewModel PropertyPanel { get; } = new();

    public MessagesViewModel Messages { get; }

    /// <summary>Applies a theme to the running app; set by the app.</summary>
    public Action<AppTheme> ApplyTheme { get; set; } = _ => { };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand), nameof(DeleteCommand), nameof(MoveUpCommand), nameof(MoveDownCommand),
                                nameof(ConnectCommand), nameof(OpenFileTransferCommand))]
    private ConnectionNodeViewModel? _selectedNode;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private bool _isDirty;

    public string? FilePath => _files.FilePath;

    public string Title => FilePath is null
        ? "mRemoteNG"
        : $"{Path.GetFileName(FilePath)}{(IsDirty ? " *" : string.Empty)} - mRemoteNG";

    private bool HasFile => _model is not null;

    private bool CanModifySelection => SelectedNode is { IsRoot: false };

    partial void OnSelectedNodeChanged(ConnectionNodeViewModel? value) => PropertyPanel.Connection = value?.Connection;

    partial void OnSearchTextChanged(string value) => ApplySearch();

    /// <summary>Whether <paramref name="source"/> may be dropped at <paramref name="position"/> of <paramref name="target"/>.</summary>
    public bool CanMove(ConnectionNodeViewModel source, ConnectionNodeViewModel target, MovePosition position) =>
        ConnectionTreeMoveRules.CanMove(source.Connection, target.Connection, position);

    /// <summary>Moves a node in the tree (drag and drop), using the same rules as the WinForms tree.</summary>
    public void Move(ConnectionNodeViewModel source, ConnectionNodeViewModel target, MovePosition position)
    {
        if (!CanMove(source, target, position))
            return;

        ConnectionInfo moved = source.Connection;
        ConnectionTreeMoveRules.Move(moved, target.Connection, position);
        if (position == MovePosition.Onto)
            target.IsExpanded = true;
        ApplySearch();
        SelectedNode = FindNode(moved);
        // Reordering within a folder keeps the same selected node, so refresh these explicitly.
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Opens the last used connection file, or the default one (created when missing).</summary>
    public async Task InitializeAsync()
    {
        if (_settings.ConnectionFile is { } lastFile && lastFile != _paths.DefaultConnectionFile)
        {
            if (File.Exists(lastFile))
            {
                await OpenAsync(lastFile);
                return;
            }

            CoreRuntime.MessageCollector.AddMessage(MessageClass.WarningMsg,
                                                    $"{lastFile} no longer exists; opening {_paths.DefaultConnectionFile} instead.", false);
        }

        if (File.Exists(_paths.DefaultConnectionFile))
            await OpenAsync(_paths.DefaultConnectionFile);
        else
            await CreateNewAsync(_paths.DefaultConnectionFile);
    }

    /// <summary>Called before the window closes; false keeps it open. Closing ends the sessions.</summary>
    public async Task<bool> CanCloseAsync()
    {
        if (Sessions.LiveCount > 0 && !await _dialogs.ConfirmAsync(UiText.Exit, UiText.ConfirmCloseSessions))
            return false;
        if (!await CanDiscardChangesAsync())
            return false;

        Sessions.CloseAll();
        return true;
    }

    private async Task<bool> CanDiscardChangesAsync()
    {
        if (!IsDirty)
            return true;
        if (_settings.SaveConnectionsAfterEveryEdit)
        {
            await SaveNowAsync(reportToUser: false);
            if (!IsDirty)
                return true;
        }

        return await _dialogs.ConfirmAsync(UiText.UnsavedChangesTitle, UiText.UnsavedChangesMessage);
    }

    [RelayCommand]
    private async Task NewFileAsync()
    {
        string? path = await _dialogs.PickConnectionFileToSaveAsync("confCons.xml");
        if (path is not null)
            await CreateNewAsync(path);
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        string? path = await _dialogs.PickConnectionFileToOpenAsync();
        if (path is not null)
            await OpenAsync(path);
    }

    [RelayCommand(CanExecute = nameof(HasFile))]
    private Task SaveAsync() => SaveNowAsync(reportToUser: true);

    [RelayCommand(CanExecute = nameof(HasFile))]
    private async Task SaveAsAsync()
    {
        string? path = await _dialogs.PickConnectionFileToSaveAsync(Path.GetFileName(FilePath ?? "confCons.xml"));
        if (path is null)
            return;

        try
        {
            _files.SaveAs(path);
            IsDirty = false;
            RememberFile(path);
        }
        catch (Exception ex)
        {
            await ReportErrorAsync(UiText.CouldNotSave(path), ex);
        }
    }

    [RelayCommand]
    private async Task OpenOptionsAsync()
    {
        OptionsViewModel options = new(_settings);
        if (!await _dialogs.ShowOptionsAsync(options))
            return;

        options.ApplyTo(_settings);
        ApplyTheme(_settings.Theme);
        SaveSettings();
    }

    /// <summary>Imports a file (mRemoteNG XML/CSV, .rdp, RDCMan, PuTTY CM, SecureCRT) into the selected folder.</summary>
    [RelayCommand(CanExecute = nameof(HasFile))]
    private async Task ImportAsync()
    {
        string? path = await _dialogs.PickFileToImportAsync();
        if (path is null)
            return;

        ConnectionNodeViewModel target = (SelectedNode ?? RootNodes.First()).TargetFolder;
        ContainerInfo folder = (ContainerInfo)target.Connection;
        ContainerInfo imported = new();
        bool passwordCancelled = false;
        try
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("The file to import does not exist.", path);

            // Importing a protected file asks for its password synchronously, so it runs off the UI thread;
            // the result is moved into the tree here, on the UI thread.
            await Task.Run(() => FileImporters.ForFile(path, () =>
            {
                Optional<SecureString> password = AskPassword(Path.GetFileName(path));
                passwordCancelled = !password.Any();
                return password;
            }).Import(path, imported));
        }
        catch (Exception ex)
        {
            if (!passwordCancelled)
                await ReportErrorAsync(UiText.CouldNotImport(path), ex);
            return;
        }

        // A connection file written by this app keeps its FTP and SFTP connections in a sidecar.
        ConnectionFileService.ApplySidecar(path, imported.GetRecursiveChildList());
        ConnectionInfo[] nodes = imported.Children.ToArray();
        foreach (ConnectionInfo node in nodes)
            folder.AddChild(node);
        target.IsExpanded = true;
        SearchText = string.Empty;
        if (nodes.Length > 0)
            SelectedNode = FindNode(nodes[^1]);
    }

    [RelayCommand(CanExecute = nameof(HasFile))]
    private void AddConnection() => AddItem(new ConnectionInfo());

    [RelayCommand(CanExecute = nameof(HasFile))]
    private void AddFolder() => AddItem(new ContainerInfo { Name = Language.NewFolder });

    [RelayCommand(CanExecute = nameof(CanModifySelection))]
    private void Duplicate()
    {
        ConnectionInfo original = SelectedNode!.Connection;
        ConnectionInfo copy = original.Clone();
        original.Parent?.AddChildBelow(copy, original);
        SelectedNode = FindNode(copy);
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => MoveBesideSibling(-1, MovePosition.Above);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => MoveBesideSibling(+1, MovePosition.Below);

    private bool CanMoveUp => SiblingOfSelection(-1) is not null;

    private bool CanMoveDown => SiblingOfSelection(+1) is not null;

    private ConnectionNodeViewModel? SiblingOfSelection(int offset)
    {
        if (SelectedNode is not { IsRoot: false, Parent: { } parent } node)
            return null;
        int index = parent.Children.IndexOf(node) + offset;
        return index >= 0 && index < parent.Children.Count ? parent.Children[index] : null;
    }

    private void MoveBesideSibling(int offset, MovePosition position)
    {
        if (SiblingOfSelection(offset) is { } sibling)
            Move(SelectedNode!, sibling, position);
    }

    [RelayCommand(CanExecute = nameof(CanModifySelection))]
    private async Task DeleteAsync()
    {
        ConnectionNodeViewModel node = SelectedNode!;
        if (!await _dialogs.ConfirmAsync(UiText.Delete, UiText.ConfirmDelete(node.Name)))
            return;

        node.Connection.Parent?.RemoveChild(node.Connection);
        SelectedNode = node.Parent;
    }

    private async Task CreateNewAsync(string path)
    {
        try
        {
            ShowModel(_files.CreateNew(path));
            RememberFile(path);
        }
        catch (Exception ex)
        {
            await ReportErrorAsync(UiText.CouldNotSave(path), ex);
        }
    }

    private async Task<bool> OpenAsync(string path)
    {
        string fileName = Path.GetFileName(path);
        try
        {
            // Deserializing asks for the password synchronously, so it runs off the UI thread while the
            // password dialog is shown on it.
            ConnectionTreeModel model = await Task.Run(() => _files.Open(path, () => AskPassword(fileName)));
            ShowModel(model);
            RememberFile(path);
            return true;
        }
        catch (Exception ex)
        {
            await ReportErrorAsync(UiText.CouldNotOpen(path), ex);
            return false;
        }
    }

    private Optional<SecureString> AskPassword(string fileName)
    {
        SecureString? password = _dialogs.AskPasswordAsync(fileName).GetAwaiter().GetResult();
        return password is null ? Optional<SecureString>.Empty : new Optional<SecureString>(password);
    }

    private void ShowModel(ConnectionTreeModel model)
    {
        if (_model is not null)
        {
            _model.CollectionChanged -= OnModelCollectionChanged;
            _model.PropertyChanged -= OnModelPropertyChanged;
        }

        foreach (ConnectionNodeViewModel node in RootNodes)
            node.Dispose();
        RootNodes.Clear();

        _model = model;
        model.CollectionChanged += OnModelCollectionChanged;
        model.PropertyChanged += OnModelPropertyChanged;
        foreach (ContainerInfo rootNode in model.RootNodes)
            RootNodes.Add(new ConnectionNodeViewModel(rootNode));

        ApplySearch();
        SelectedNode = RootNodes.FirstOrDefault();
        IsDirty = false;
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(Title));
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
        NotifyTreeCommands();
        AddConnectionCommand.NotifyCanExecuteChanged();
        AddFolderCommand.NotifyCanExecuteChanged();
    }

    private void AddItem(ConnectionInfo item)
    {
        ConnectionNodeViewModel target = (SelectedNode ?? RootNodes.First()).TargetFolder;
        ((ContainerInfo)target.Connection).AddChild(item);
        target.IsExpanded = true;
        SearchText = string.Empty; // a new item would not match the search
        SelectedNode = FindNode(item);
    }

    private void ApplySearch()
    {
        foreach (ConnectionNodeViewModel root in RootNodes)
            root.ApplyFilter(SearchText);
    }

    private ConnectionNodeViewModel? FindNode(ConnectionInfo connection) =>
        RootNodes.SelectMany(root => root.SelfAndDescendants()).FirstOrDefault(node => node.Connection == connection);

    private void OnModelCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => MarkChanged();

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ConnectionInfo.OpenConnections))
            MarkChanged();
    }

    private void MarkChanged()
    {
        IsDirty = true;
        if (!_settings.SaveConnectionsAfterEveryEdit || _changingFilePassword || _savePending)
            return;

        // One save for a burst of changes (e.g. a property edit and the follow-ups it triggers).
        _savePending = true;
        _dispatch(() =>
        {
            _savePending = false;
            _ = SaveNowAsync(reportToUser: false);
        });
    }

    private async Task SaveNowAsync(bool reportToUser)
    {
        if (_model is null || _changingFilePassword)
            return;

        try
        {
            _files.Save();
            IsDirty = false;
        }
        catch (Exception ex)
        {
            if (reportToUser)
                await ReportErrorAsync(UiText.CouldNotSave(FilePath ?? string.Empty), ex);
            else
                CoreRuntime.MessageCollector.AddExceptionMessage(UiText.CouldNotSave(FilePath ?? string.Empty), ex,
                                                                 MessageClass.ErrorMsg, logOnly: false);
        }
    }

    private async void OnPropertyEdited(object? sender, PropertyItemViewModel item)
    {
        // Inheritance settings raise no change events of their own.
        if (item.IsInheritanceSetting)
        {
            MarkChanged();
            return;
        }

        if (item.Name != nameof(RootNodeInfo.Password) || PropertyPanel.Connection is not RootNodeInfo rootNode)
            return;

        // RootNodeInfo.Password/PasswordString raise no change events, so mark the change explicitly.
        if (!rootNode.Password)
        {
            rootNode.PasswordString = string.Empty;
            MarkChanged();
            return;
        }

        // Don't save until the password is known, or the file would be protected by an empty one.
        _changingFilePassword = true;
        SecureString? password;
        try
        {
            password = await _dialogs.AskNewPasswordAsync(Path.GetFileName(FilePath ?? string.Empty));
        }
        finally
        {
            _changingFilePassword = false;
        }

        if (password is null || password.Length == 0)
        {
            rootNode.Password = false;
            item.RefreshValue();
        }
        else
        {
            rootNode.PasswordString = password.ConvertToUnsecureString();
        }

        MarkChanged();
    }

    private void RememberFile(string path)
    {
        _settings.ConnectionFile = path;
        SaveSettings();
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CoreRuntime.MessageCollector.AddExceptionMessage("Could not save the settings.", ex, MessageClass.WarningMsg, logOnly: false);
        }
    }

    private async Task ReportErrorAsync(string title, Exception ex)
    {
        CoreRuntime.MessageCollector.AddExceptionMessage(title, ex, MessageClass.ErrorMsg, logOnly: false);
        await _dialogs.ShowErrorAsync(title, ex.GetExceptionMessageRecursive());
    }
}
