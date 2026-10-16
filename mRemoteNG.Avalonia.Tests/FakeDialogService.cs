using System.Security;
using mRemoteNG.Avalonia.Services;
using mRemoteNG.Security;

namespace mRemoteNG.Avalonia.Tests;

/// <summary>Answers the view model's dialogs with preset values and records what was shown.</summary>
internal sealed class FakeDialogService : IDialogService
{
    public string? FileToOpen { get; set; }
    public string? FileToSave { get; set; }
    public Queue<string?> Passwords { get; } = new();
    public string? NewPassword { get; set; }
    public bool ConfirmAnswer { get; set; } = true;
    public List<string> Errors { get; } = [];
    public List<string> Confirmations { get; } = [];

    public Task<string?> PickConnectionFileToOpenAsync() => Task.FromResult(FileToOpen);

    public Task<string?> PickConnectionFileToSaveAsync(string suggestedFileName) => Task.FromResult(FileToSave);

    public string? FileToImport { get; set; }

    public Task<string?> PickFileToImportAsync() => Task.FromResult(FileToImport);

    public Task<SecureString?> AskPasswordAsync(string fileName) =>
        Task.FromResult(Passwords.Count > 0 && Passwords.Dequeue() is { } password ? password.ConvertToSecureString() : null);

    public Task<SecureString?> AskNewPasswordAsync(string fileName) => Task.FromResult(NewPassword?.ConvertToSecureString());

    public Task<bool> ConfirmAsync(string title, string message)
    {
        Confirmations.Add(message);
        return Task.FromResult(ConfirmAnswer);
    }

    public Func<ViewModels.OptionsViewModel, bool> EditOptions { get; set; } = _ => false;

    public Task<bool> ShowOptionsAsync(ViewModels.OptionsViewModel options) => Task.FromResult(EditOptions(options));

    public Task ShowErrorAsync(string title, string message)
    {
        Errors.Add($"{title}: {message}");
        return Task.CompletedTask;
    }
}
