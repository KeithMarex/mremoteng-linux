using System.Security;
using mRemoteNG.FileTransfer;

namespace mRemoteNG.Avalonia.Services;

/// <summary>A login: what a connection stores, or what the user typed when asked.</summary>
public sealed record LoginCredentials(string Username, string Domain, string Password);

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

    /// <summary>Asks for a secret, such as a server password or a key passphrase; null when cancelled. Callable from any thread.</summary>
    Task<SecureString?> AskSecretAsync(string prompt);

    /// <summary>Asks for a user name, domain and password, starting from the given ones; null when cancelled.</summary>
    Task<LoginCredentials?> AskLoginAsync(string prompt, LoginCredentials initial);

    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>Asks whether to trust a server's host key or certificate. Callable from any thread.</summary>
    Task<TrustDecision> AskTrustAsync(string title, string message);

    /// <summary>Asks for one line of text, such as a new name; null when cancelled.</summary>
    Task<string?> AskTextAsync(string title, string prompt, string initialText);

    /// <summary>Shows the External Tools window; true when the user kept the changes.</summary>
    Task<bool> ShowExternalToolsAsync(ViewModels.ExternalToolsViewModel tools);

    /// <summary>Shows the options; true when the user confirmed them.</summary>
    Task<bool> ShowOptionsAsync(ViewModels.OptionsViewModel options);

    Task ShowErrorAsync(string title, string message);
}
