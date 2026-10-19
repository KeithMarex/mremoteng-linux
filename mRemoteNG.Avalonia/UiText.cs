using mRemoteNG.FileTransfer;
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
    public static string Username => Language.Username;
    public static string Domain => Language.Domain;
    public static string Search => Language.SearchPrompt;
    public static string MoveUp => Language.MoveUp;
    public static string MoveDown => Language.MoveDown;
    public static string View => Menu(Language._View);
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
    public static string Config => Language.Config;
    public static string PropertiesView => Language.Properties;
    public static string InheritanceView => Language.Inheritance;
    public static string ExpandAllFolders => Language.ExpandAllFolders;
    public static string CollapseAllFolders => Language.CollapseAllFolders;
    public static string Sort => Language.Sort;
    public static string Favorites => Language.Favorites;
    public static string Reconnect => Language.Reconnect;
    public static string CloseTab => Language._Close.Replace("&", string.Empty);
    public static string ConfirmCloseSessions => Language.ConfirmExitMainInstruction;
    public static string Connecting => Language.Connecting;
    public static string Download => Language.Download;
    public static string Rename => Language.Rename;

    public const string Edit = "_Edit";
    public const string ConnectionFile = "Connection file";
    public const string Security = "Security";
    public const string FileFormat = "File format version";
    public const string FileFormatHelp =
        "Use the version every Windows mRemoteNG in your team can read. Older versions store fewer settings " +
        "(2.7 lacks e.g. tab colours); the change applies the next time the file is saved.";
    public const string Ok = "OK";
    public const string Cancel = "Cancel";
    public const string Yes = "Yes";
    public const string No = "No";
    public const string ConfirmPassword = "Confirm password";
    public const string PasswordsDoNotMatch = "The passwords do not match.";
    public const string NoSessionsYet =
        "Double-click a connection to open it here. SSH, Telnet, Rlogin, Raw, local Terminal, PowerShell, SFTP and FTP " +
        "connections open in a tab; HTTP, HTTPS and AnyDesk open in their own window.";
    public const string Disconnected = "Disconnected.";
    public const string NoFavorites = "No favorites yet: set Favorite to Yes in a connection's properties.";
    public const string Categorized = "Categorized";
    public const string Alphabetical = "Alphabetical";

    // File transfer
    public const string OpenFileTransfer = "Open SFTP";
    public const string ThisComputer = "This computer";
    public const string Transfers = "Transfers";
    public const string ClearFinished = "Clear finished";
    public const string Upload = "Upload";
    public const string Move = "Move";
    public const string PropertyDescription = "Property Description";
    public const string Refresh = "Refresh";
    public const string Up = "Up";
    public const string NewFolderPrompt = "Name of the new folder:";
    public const string NotConnected = "Not connected.";
    public const string ConnectionCancelled = "Connecting was cancelled.";
    public const string UnencryptedWarning = "Unencrypted FTP: the password and the files cross the network in plain text.";
    public const string UnsafePassword = "The password contains a line break, which FTP cannot send.";
    public const string TransferQueued = "Queued";
    public const string TransferPreparing = "Preparing…";
    public const string TransferCancelled = "Cancelled";
    public const string TrustAndRemember = "Trust and remember";
    public const string TrustOnce = "Trust this time";
    public const string UnknownHostKeyTitle = "Unknown host key";
    public const string UntrustedCertificateTitle = "Untrusted certificate";
    public const string FileTransferCategory = "File transfer";
    public const string FtpEncryptionName = "Encryption";
    public const string FtpEncryptionDescription =
        "Explicit TLS (FTPS) is what most servers offer; implicit TLS usually runs on port 990. Without encryption the password crosses the network in plain text.";
    public const string FtpPassiveName = "Passive mode";
    public const string FtpPassiveDescription = "Passive mode works through NAT and most firewalls; turn it off only for servers that need active mode.";
    public const string FtpExplicitTls = "Explicit TLS (FTPS)";
    public const string FtpImplicitTls = "Implicit TLS (FTPS, port 990)";
    public const string FtpNoEncryption = "None (unencrypted)";
    public const string StartDirectoryName = "Start directory";
    public const string StartDirectoryDescription = "The server directory the tab opens in; empty for the user's home directory.";
    public const string TerminalShortcuts = "Ctrl+Shift+C copies the selection, Ctrl+Shift+V pastes, Ctrl+Shift+W closes the tab.";
    public const string Ssh1NotSupported = "SSH version 1 is not supported by OpenSSH. Change the connection's protocol to SSH version 2.";
    public const string UnsafeHostname = "The host name contains characters that are not allowed (spaces, control characters or a leading '-').";
    public const string UnsafeUsername = "The user name contains characters that are not allowed (spaces, control characters or a leading '-').";
    public const string PuttySessionIgnored = "The PuTTY session name is not a valid ssh_config host name and was left out.";
    public const string OpeningCommandNotSupported = "The opening command is not sent yet by mRemoteNG for Linux.";
    public const string StoredPasswordUnavailable = "The stored password cannot be handed to ssh; type it in the terminal.";
    public const string WslWindowsOnly = "WSL connections open a Linux distribution on a Windows computer; they exist only on Windows.";
    public const string ServerClosedConnection = "The server closed the connection.";
    public const string Login = "Log in";
    public const string FreeRdpNotFound =
        "RDP needs FreeRDP 3: install the freerdp3-x11 package (on Debian 12 from bookworm-backports).";
    public const string RdpInOwnWindow = "The remote desktop is open in its own full-screen window.";
    public const string RdpCustomDrivesAsHome = "Drive letters do not exist on Linux; your home folder is shared instead of the chosen drives.";
    public const string RdpGatewayWithoutHost = "The Remote Desktop Gateway has no host name and is not used.";
    public const string RdpGatewayLoginIgnored =
        "The gateway's login (smart card or external credential provider) is not supported here; the gateway asks for none.";
    public const string RdpGatewayValueWithComma =
        "A gateway setting (host, user name, domain, password or access token) contains a comma or quote, which xfreerdp cannot take.";
    public const string ExternalCredentialProviderIgnored =
        "External credential providers are not supported by mRemoteNG for Linux yet; the connection's own login is used.";
    public const string NotAnRdpServer = "the server did not answer as a Remote Desktop server";
    public const string NoServerCertificate = "the server sent no certificate";
    public const string CertificateRejected = "The server's certificate was not accepted.";
    public const string CertificateNameMismatch = "it is issued to another name";
    public const string CertificateNotTrusted = "it is not issued by an authority this computer trusts (it may be self-signed or expired)";
    public const string NoRloginClient =
        "No rlogin client is installed, so mRemoteNG connects itself. Most rlogin servers only accept the rlogin client, " +
        "which can use a privileged port; install the rsh-redone-client package where your distribution has it.";
    public const string InvalidWebAddress = "The host name does not make a valid http or https address.";
    public const string InvalidAnyDeskId =
        "The host name is not a valid AnyDesk ID: only letters, digits, '@', '-', '_' and '.' are allowed, and it may not start with '-'.";
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
    public static string RloginConnection(string host, string port) => $"rlogin {host}:{port}";
    public static string NeedsAppExecutable(string protocol) =>
        $"{protocol} connections need mRemoteNG to run from its own executable, not through \"dotnet\".";
    public static string PowerShellUsesSsh(int port) =>
        $"PowerShell on Linux connects over SSH (port 22), not WinRM; port {port} is not used. The server needs PowerShell's SSH subsystem.";
    public static string RdpLoginPrompt(string host) => $"Log in to {host}:";
    public static string RdpSettingIgnored(string setting) => $"The RDP setting \"{setting}\" is not supported by xfreerdp and was left out.";
    public static string RdpValueWithLineBreak(string option) => $"The value for {option} contains a line break, which xfreerdp cannot take.";
    public static string CertificateNotRead(string host, string reason) =>
        $"The certificate of {host} could not be read beforehand ({reason}); xfreerdp checks it itself.";
    public static string ConnectionLost(string reason) => $"The connection was lost: {reason}";
    public static string CouldNotStart(string program, string reason) => $"Could not start {program}: {reason}";
    public static string Opening(string name, string commandLine) => $"Opening {name}: {commandLine}";
    public static string ProgramEnded(string program, int exitCode) => $"{program} ended with exit code {exitCode}.";
    public static string ConnectedTo(string server) => $"Connected to {server}.";
    public static string CouldNotList(string path, string reason) => $"Could not open {path}: {reason}";
    public static string ConfirmOverwrite(int count, string directory) => count == 1
        ? $"1 item already exists in {directory}. Replace it? (No skips it.)"
        : $"{count} items already exist in {directory}. Replace them? (No skips them.)";
    public static string RenamePrompt(string name) => $"New name for {name}:";
    public static string ItemCount(int count) => $"{count} items";
    public static string NotMovedAlreadyThere(IReadOnlyList<string> names, string directory) => names.Count == 1
        ? $"\"{names[0]}\" already exists in {directory} and was not moved."
        : $"{string.Join(", ", names.Select(name => $"\"{name}\""))} already exist in {directory} and were not moved.";
    public static string ConfirmDeleteOnServer(string what) => $"Delete {what} from the server? This cannot be undone.";
    public static string InvalidFileName(string name) => $"\"{name}\" is not a valid name: it may not contain '/' or be '.' or '..'.";
    public static string TransferProgress(string done, string total, string? file) =>
        file is null ? $"{done} of {total}" : $"{done} of {total} · {file}";
    public static string TransferDone(int skipped) => skipped == 0 ? "Done" : $"Done; {skipped} skipped (already there)";
    public static string TransferFailed(string error) => $"Failed: {error}";
    public static string PassphrasePrompt(string keyFile) => $"Enter the passphrase for the key {keyFile}:";
    public static string SftpIgnoresProxy(string proxy) => $"ssh_config sets {proxy}; SFTP connects to the host directly.";

    public static string UnknownHostKey(HostKeyQuestion question)
    {
        string host = question.Port == 22 ? question.Host : $"[{question.Host}]:{question.Port}";
        string otherTypes = question.KnownKeyTypes.Count == 0
            ? string.Empty
            : $"\nThe host is known, but with another key type ({string.Join(", ", question.KnownKeyTypes)}).";
        string keyFile = question.KeyType switch
        {
            "ssh-rsa" => "rsa",
            _ when question.KeyType.StartsWith("ecdsa", StringComparison.Ordinal) => "ecdsa",
            _ => "ed25519"
        };
        return $"The authenticity of host {host} can't be established.\n{question.KeyType} key fingerprint is {question.Fingerprint}.{otherTypes}\n\n" +
               $"Only trust it if this is your server's fingerprint (on the server: ssh-keygen -lf /etc/ssh/ssh_host_{keyFile}_key.pub).";
    }

    public static string UntrustedCertificate(CertificateQuestion question) =>
        $"The certificate of {question.Host}:{question.Port} is not trusted by this computer: {question.Problems}.\n\n" +
        $"Issued to: {question.Certificate.Subject}\nIssued by: {question.Certificate.Issuer}\n" +
        $"Valid: {question.Certificate.NotBefore:d} to {question.Certificate.NotAfter:d}\nSHA-256: {question.Fingerprint}\n\n" +
        "Only trust it if you expected this certificate, such as your own server's self-signed one.";
}
