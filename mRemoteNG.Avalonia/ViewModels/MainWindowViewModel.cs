using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Platform;
using mRemoteNG.Avalonia.Services;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Avalonia.ViewModels;

/// <summary>The main window: the connection tree, its properties, notifications and the file commands.</summary>
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
    public MainWindowViewModel(AppSettings settings, AppSettingsStore settingsStore, AppPaths paths,
                               IDialogService dialogs, Action<Action> dispatch)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _paths = paths;
        _dialogs = dialogs;
        _dispatch = dispatch;
        _files = new ConnectionFileService(settings);
        Messages = new MessagesViewModel(CoreRuntime.MessageCollector, dispatch);
        PropertyPanel.PropertyEdited += OnPropertyEdited;
    }

    public ObservableCollection<ConnectionNodeViewModel> RootNodes { get; } = [];

    public PropertyPanelViewModel PropertyPanel { get; } = new();

    public MessagesViewModel Messages { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand), nameof(DeleteCommand))]
    private ConnectionNodeViewModel? _selectedNode;

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

    /// <summary>Called before the window closes; false keeps it open.</summary>
    public async Task<bool> CanCloseAsync()
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

        SelectedNode = RootNodes.FirstOrDefault();
        IsDirty = false;
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(Title));
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        AddConnectionCommand.NotifyCanExecuteChanged();
        AddFolderCommand.NotifyCanExecuteChanged();
    }

    private void AddItem(ConnectionInfo item)
    {
        ConnectionNodeViewModel target = (SelectedNode ?? RootNodes.First()).TargetFolder;
        ((ContainerInfo)target.Connection).AddChild(item);
        target.IsExpanded = true;
        SelectedNode = FindNode(item);
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
