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

    public const string Edit = "_Edit";
    public const string Inherit = "Inherit";
    public const string Ok = "OK";
    public const string Cancel = "Cancel";
    public const string Yes = "Yes";
    public const string No = "No";
    public const string ConfirmPassword = "Confirm password";
    public const string PasswordsDoNotMatch = "The passwords do not match.";
    public const string NoSessionsYet = "Open a connection from the tree. Sessions (SSH, Telnet, SFTP, FTP) open here in a later version.";
    public const string UnsavedChangesTitle = "Unsaved changes";
    public const string UnsavedChangesMessage = "The connection file has unsaved changes. Close without saving?";

    public static string PasswordPrompt(string fileName) => $"Enter the password for {fileName}.";
    public static string NewPasswordPrompt(string fileName) => $"Choose a password to protect {fileName}.";
    public static string ConfirmDelete(string name) => $"Delete \"{name}\"?";
    public static string CouldNotOpen(string path) => $"Could not open {path}";
    public static string CouldNotSave(string path) => $"Could not save {path}";
}
