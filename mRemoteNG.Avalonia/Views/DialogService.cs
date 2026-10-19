using System.Security;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using mRemoteNG.Avalonia.Services;
using mRemoteNG.Config.Import;
using mRemoteNG.FileTransfer;
using mRemoteNG.Resources.Language;

namespace mRemoteNG.Avalonia.Views;

/// <summary>Shows the app's dialogs over the main window; every method may be called from any thread.</summary>
public sealed class DialogService(Window owner) : IDialogService
{
    private static readonly FilePickerFileType ConnectionFiles = new("mRemoteNG connection files") { Patterns = ["*.xml"] };

    public Task<string?> PickConnectionFileToOpenAsync() => OnUiThread(async () =>
    {
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = UiText.OpenConnectionFile,
            AllowMultiple = false,
            FileTypeFilter = [ConnectionFiles, FilePickerFileTypes.All]
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    });

    public Task<string?> PickConnectionFileToSaveAsync(string suggestedFileName) => OnUiThread(async () =>
    {
        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = UiText.SaveConnectionFileAs,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "xml",
            FileTypeChoices = [ConnectionFiles],
            ShowOverwritePrompt = true
        });
        return file?.TryGetLocalPath();
    });

    public Task<string?> PickFileToImportAsync() => OnUiThread(async () =>
    {
        FilePickerFileType importable = new(Language.FilterAllImportable)
        {
            Patterns = FileImporters.SupportedExtensions.Keys.Select(extension => "*" + extension).ToArray()
        };
        List<FilePickerFileType> filters = [importable];
        filters.AddRange(FileImporters.SupportedExtensions.Select(format =>
            new FilePickerFileType($"{format.Value} (*{format.Key})") { Patterns = ["*" + format.Key] }));

        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = UiText.ImportFileTitle,
            AllowMultiple = false,
            FileTypeFilter = filters
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    });

    public Task<SecureString?> AskPasswordAsync(string fileName) =>
        OnUiThread(() => new PasswordDialog(UiText.PasswordPrompt(fileName), confirm: false).ShowDialog<SecureString?>(owner));

    public Task<SecureString?> AskNewPasswordAsync(string fileName) =>
        OnUiThread(() => new PasswordDialog(UiText.NewPasswordPrompt(fileName), confirm: true).ShowDialog<SecureString?>(owner));

    public Task<SecureString?> AskSecretAsync(string prompt) =>
        OnUiThread(() => new PasswordDialog(prompt, confirm: false).ShowDialog<SecureString?>(owner));

    public Task<LoginCredentials?> AskLoginAsync(string prompt, LoginCredentials initial) =>
        OnUiThread(() => new LoginDialog(prompt, initial).ShowDialog<LoginCredentials?>(owner));

    public Task<TrustDecision> AskTrustAsync(string title, string message) =>
        OnUiThread(() => new TrustDialog(title, message).ShowDialog<TrustDecision>(owner));

    public Task<string?> AskTextAsync(string title, string prompt, string initialText) =>
        OnUiThread(() => new TextInputDialog(title, prompt, initialText).ShowDialog<string?>(owner));

    public Task<bool> ConfirmAsync(string title, string message) =>
        OnUiThread(() => new MessageDialog(title, message, isQuestion: true).ShowDialog<bool>(owner));

    public Task<bool> ShowExternalToolsAsync(ViewModels.ExternalToolsViewModel tools) =>
        OnUiThread(() => new ExternalToolsWindow { DataContext = tools }.ShowDialog<bool>(owner));

    public Task<bool> ShowOptionsAsync(ViewModels.OptionsViewModel options) =>
        OnUiThread(() => new OptionsWindow { DataContext = options }.ShowDialog<bool>(owner));

    public Task ShowErrorAsync(string title, string message) =>
        OnUiThread(() => new MessageDialog(title, message, isQuestion: false).ShowDialog<bool>(owner));

    private static Task<T> OnUiThread<T>(Func<Task<T>> show) =>
        Dispatcher.UIThread.CheckAccess() ? show() : Dispatcher.UIThread.InvokeAsync(show);
}
