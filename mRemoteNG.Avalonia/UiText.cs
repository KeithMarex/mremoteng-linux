using mRemoteNG.Resources.Language;

namespace mRemoteNG.Avalonia;

/// <summary>
/// UI texts of the Linux app. Where mRemoteNG's translations (Core's Language resources) already have a
/// text it is used, so it appears in the user's language; the rest is English until it is added there.
/// </summary>
public static class UiText
{
    /// <summary>Turns a WinForms mnemonic ("&amp;File") into an Avalonia access key ("_File").</summary>
    public static string Menu(string text) => text.Replace("&&", "\u0001").Replace('&', '_').Replace("\u0001", "&");

    public static string File => Menu(Language._File);
    public static string Help => Menu(Language._Help);
    public static string NewConnectionFile => Language.NewConnectionFile;
    public static string OpenConnectionFile => Language.OpenConnectionFile;
    public static string SaveConnectionFile => Language.SaveConnectionFile;
    public static string SaveConnectionFileAs => Language.SaveConnectionFileAs;
    public static string Exit => Language.Exit;
    public static string NewConnection => Language.NewConnection;
    public static string NewFolder => Language.NewFolder;
    public static string Duplicate => Language.Duplicate;
    public static string Delete => Language.Delete;
    public static string Connections => Language.Connections;
    public static string Properties => Language.Properties;
    public static string Notifications => Language.Notifications;
    public static string Password => Language.Password;
    public static string Search => Language.SearchPrompt;
    public static string MoveUp => Language.MoveUp;
    public static string MoveDown => Language.MoveDown;
    public static string Tools => Menu(Language._Tools);
    public static string ImportFromFile => Menu(Language.ImportFromFile);
    public static string ImportFileTitle => Language.ImportFromFile.Replace("&", string.Empty).TrimEnd('.');
    public static string Options => Language.Options;
    public static string Theme => Language.Theme;
    public static string Appearance => Language.Appearance;
    public static string EncryptionEngine => Language.EncryptionEngine;
    public static string EncryptionBlockCipherMode => Language.EncryptionBlockCipherMode;
    public static string EncryptionKeyDerivationIterations => Language.EncryptionKeyDerivationIterations;
    public static string EncryptCompleteConnectionFile => Language.EncryptCompleteConnectionFile;
    public static string SaveConnectionsAfterEveryEdit => Language.SaveConnectionsAfterEveryEdit;
    public static string Connect => Language.Connect;
    public static string Reconnect => Language.Reconnect;
    public static string CloseTab => Language._Close.Replace("&", string.Empty);
    public static string ConfirmCloseSessions => Language.ConfirmExitMainInstruction;

    public const string Edit = "_Edit";
    public const string ConnectionFile = "Connection file";
    public const string Security = "Security";
    public const string FileFormat = "File format version";
    public const string FileFormatHelp =
        "Use the version every Windows mRemoteNG in your team can read. Older versions store fewer settings " +
        "(2.7 lacks e.g. tab colours); the change applies the next time the file is saved.";
    public const string Inherit = "Inherit";
    public const string Ok = "OK";
    public const string Cancel = "Cancel";
    public const string Yes = "Yes";
    public const string No = "No";
    public const string ConfirmPassword = "Confirm password";
    public const string PasswordsDoNotMatch = "The passwords do not match.";
    public const string NoSessionsYet =
        "Double-click a connection to open it here. SSH, Telnet, Raw and local Terminal connections are supported; SFTP and FTP follow in a later version.";
    public const string Disconnected = "Disconnected.";
    public const string TerminalShortcuts = "Ctrl+Shift+C copies the selection, Ctrl+Shift+V pastes, Ctrl+Shift+W closes the tab.";
    public const string Ssh1NotSupported = "SSH version 1 is not supported by OpenSSH. Change the connection's protocol to SSH version 2.";
    public const string UnsafeHostname = "The host name contains characters that are not allowed (spaces, control characters or a leading '-').";
    public const string UnsafeUsername = "The user name contains characters that are not allowed (spaces, control characters or a leading '-').";
    public const string PuttySessionIgnored = "The PuTTY session name is not a valid ssh_config host name and was left out.";
    public const string OpeningCommandNotSupported = "The opening command is not sent yet by mRemoteNG for Linux.";
    public const string StoredPasswordUnavailable = "The stored password cannot be handed to ssh; type it in the terminal.";
    public const string NeedsAppExecutable = "Raw connections need mRemoteNG to run from its own executable, not through \"dotnet\".";
    public const string UnsavedChangesTitle = "Unsaved changes";
    public const string UnsavedChangesMessage = "The connection file has unsaved changes. Close without saving?";

    public static string PasswordPrompt(string fileName) => $"Enter the password for {fileName}.";
    public static string NewPasswordPrompt(string fileName) => $"Choose a password to protect {fileName}.";
    public static string ConfirmDelete(string name) => $"Delete \"{name}\"?";
    public static string CouldNotOpen(string path) => $"Could not open {path}";
    public static string CouldNotSave(string path) => $"Could not save {path}";
    public static string CouldNotImport(string path) => $"Could not import {path}";
    public static string ProtocolNotSupported(string protocol) => $"{protocol} connections cannot be opened in mRemoteNG for Linux yet.";
    public static string InvalidPort(int port) => $"{port} is not a valid port number.";
    public static string ProgramNotFound(string program, string? package) =>
        package is null ? $"{program} was not found." : $"{program} was not found; install the {package} package.";
    public static string SshOptionIgnored(string option) => $"The SSH option {option} has no OpenSSH equivalent here and was left out.";
    public static string ConnectingTo(string name, string commandLine) => $"Connecting to {name}: {commandLine}";
    public static string SessionEnded(int? exitCode) =>
        exitCode is null ? "The session ended." : $"The session ended (exit code {exitCode}).";
    public static string CouldNotConnect(string reason) => $"{Language.ConnectionOpenFailed} {reason}";
    public static string CouldNotConnectTo(string host, int port, string reason) => $"Could not connect to {host}:{port}: {reason}";
    public static string RawConnection(string host, string port) => $"raw TCP {host}:{port}";
}
