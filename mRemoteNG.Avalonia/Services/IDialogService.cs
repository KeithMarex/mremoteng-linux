using System.Security;

namespace mRemoteNG.Avalonia.Services;

/// <summary>The dialogs the main window needs; an interface so view models can be tested without a UI.</summary>
public interface IDialogService
{
    Task<string?> PickConnectionFileToOpenAsync();

    Task<string?> PickConnectionFileToSaveAsync(string suggestedFileName);

    /// <summary>Asks for a file to import (any format Core's FileImporters understands).</summary>
    Task<string?> PickFileToImportAsync();

    /// <summary>Asks for the password of a protected connection file; null when cancelled. Callable from any thread.</summary>
    Task<SecureString?> AskPasswordAsync(string fileName);

    /// <summary>Asks for a new password (entered twice); null when cancelled.</summary>
    Task<SecureString?> AskNewPasswordAsync(string fileName);

    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>Shows the options; true when the user confirmed them.</summary>
    Task<bool> ShowOptionsAsync(ViewModels.OptionsViewModel options);

    Task ShowErrorAsync(string title, string message);
}
