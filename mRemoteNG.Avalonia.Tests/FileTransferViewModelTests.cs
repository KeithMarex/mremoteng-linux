using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Avalonia.ViewModels;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.FileTransfer;
using mRemoteNG.FileTransfer.Ftp;
using mRemoteNG.FileTransfer.Tests;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class FileTransferViewModelTests
{
    private TempDirectory _local = null!;
    private FakeRemoteFileSystem _server = null!;
    private FakeDialogService _dialogs = null!;
    private ConnectionInfo _connection = null!;
    private FileTransferViewModel _tab = null!;

    [SetUp]
    public async Task Setup()
    {
        _local = new TempDirectory();
        _server = new FakeRemoteFileSystem();
        _server.AddFile("/home/tester/readme.txt", "hello");
        _server.AddDirectory("/home/tester/upload");
        _dialogs = new FakeDialogService();
        _connection = new ConnectionInfo { Name = "files", Protocol = ProtocolType.SFTP, Hostname = "files.example" };
        _tab = Tab(_ => Task.FromResult<IRemoteFileSystem>(_server), startDirectory: "/home/tester");
        await _tab.Local.NavigateAsync(_local.Path);
        await _tab.ConnectAsync();
    }

    [TearDown]
    public void Teardown()
    {
        _tab.Dispose();
        _local.Dispose();
    }

    [Test]
    public void ConnectingShowsTheStartDirectoryAndCountsAsAnOpenConnection()
    {
        Assert.That(_tab.State, Is.EqualTo(FileTransferState.Connected));
        Assert.That(_tab.Remote.CurrentPath, Is.EqualTo("/home/tester"));
        Assert.That(_tab.Remote.Entries.Select(entry => entry.Name), Is.EqualTo(new[] { "..", "upload", "readme.txt" }),
                    "the directory above, then directories");
        Assert.That(_connection.OpenConnections.Count, Is.EqualTo(1));
        Assert.That(_tab.IsUnencrypted, Is.False);
    }

    [Test]
    public async Task UploadingPutsTheFileInTheRemoteDirectoryAndShowsIt()
    {
        File.WriteAllText(_local.File("notes.txt"), "uploaded");
        await _tab.Local.RefreshAsync();
        _tab.Local.SelectedEntries.Add(_tab.Local.Entries.Single(entry => !entry.IsParent));

        await _tab.UploadCommand.ExecuteAsync(null);
        await Finished(_tab.Transfers.Single());

        Assert.That(_server.Content("/home/tester/notes.txt"), Is.EqualTo("uploaded"));
        Assert.That(_tab.Remote.Entries.Select(entry => entry.Name), Does.Contain("notes.txt"));
    }

    [Test]
    public async Task DownloadingOverAnExistingFileAsksAndNoSkipsIt()
    {
        File.WriteAllText(_local.File("readme.txt"), "mine");
        _dialogs.ConfirmAnswer = false;
        _tab.Remote.SelectedEntries.Add(_tab.Remote.Entries.Single(entry => entry.Name == "readme.txt"));

        await _tab.DownloadCommand.ExecuteAsync(null);
        await Finished(_tab.Transfers.Single());

        Assert.That(_dialogs.Confirmations.Single(), Does.Contain("already exists"));
        Assert.That(File.ReadAllText(_local.File("readme.txt")), Is.EqualTo("mine"));
        Assert.That(_tab.Transfers.Single().StateText, Does.Contain("1 skipped"));
    }

    [Test]
    public async Task FoldersAreCreatedRenamedAndDeletedOnTheServer()
    {
        _dialogs.Texts.Enqueue("reports");
        await _tab.NewFolderCommand.ExecuteAsync(null);
        Assert.That(_tab.Remote.Entries.Select(entry => entry.Name), Does.Contain("reports"));

        _tab.Remote.SelectedEntries.Add(_tab.Remote.Entries.Single(entry => entry.Name == "reports"));
        await _tab.DeleteCommand.ExecuteAsync(null);

        Assert.That(_dialogs.Confirmations.Single(), Does.Contain("reports"));
        Assert.That(_tab.Remote.Entries.Select(entry => entry.Name), Does.Not.Contain("reports"));
    }

    [Test]
    public async Task ANameThatIsNotASinglePathSegmentIsRefused()
    {
        _dialogs.Texts.Enqueue("../outside");

        await _tab.NewFolderCommand.ExecuteAsync(null);

        Assert.That(_dialogs.Errors.Single(), Does.Contain("not a valid name"));
        Assert.That(await _server.ExistsAsync("/home/outside", CancellationToken.None), Is.False);
    }

    [Test]
    public async Task TheParentEntryOpensTheDirectoryAboveAndIsNotThereAtTheTop()
    {
        await _tab.Remote.OpenCommand.ExecuteAsync(_tab.Remote.Entries[0]);
        Assert.That(_tab.Remote.CurrentPath, Is.EqualTo("/home"));

        await _tab.Remote.NavigateAsync("/");
        Assert.That(_tab.Remote.Entries.Any(entry => entry.IsParent), Is.False);
    }

    [Test]
    public async Task DroppingOnTheParentMovesUpOnTheServer()
    {
        _server.AddFile("/home/tester/upload/report.txt", "q3");
        _server.AddFile("/home/tester/upload/old/2025.txt", "q4");
        await _tab.Remote.NavigateAsync("/home/tester/upload");

        await _tab.MoveAsync(_tab.Remote, _tab.Remote.Entries.Where(entry => !entry.IsParent).ToList(), _tab.Remote.Entries[0].FullPath);

        Assert.That(_server.Content("/home/tester/report.txt"), Is.EqualTo("q3"));
        Assert.That(_server.Content("/home/tester/old/2025.txt"), Is.EqualTo("q4"));
        Assert.That(_tab.Remote.Entries.Select(entry => entry.Name), Is.EqualTo(new[] { ".." }));
    }

    [Test]
    public async Task MovingOntoANameThatIsThereLeavesItWhereItWas()
    {
        _server.AddFile("/home/tester/upload/readme.txt", "newer");
        await _tab.Remote.NavigateAsync("/home/tester/upload");

        await _tab.MoveAsync(_tab.Remote, [_tab.Remote.Entries.Single(entry => entry.Name == "readme.txt")], "/home/tester");

        Assert.That(_dialogs.Errors.Single(), Does.Contain("already exists"));
        Assert.That(_server.Content("/home/tester/readme.txt"), Is.EqualTo("hello"));
        Assert.That(_server.Content("/home/tester/upload/readme.txt"), Is.EqualTo("newer"));
    }

    [Test]
    public void ADirectoryDoesNotMoveIntoItselfOrWhereItIs()
    {
        FileEntryViewModel upload = _tab.Remote.Entries.Single(entry => entry.Name == "upload");

        Assert.That(_tab.Remote.CanMoveInto(upload, "/home/tester/upload"), Is.False);
        Assert.That(_tab.Remote.CanMoveInto(upload, "/home/tester/upload/sub"), Is.False);
        Assert.That(_tab.Remote.CanMoveInto(upload, "/home/tester"), Is.False);
        Assert.That(_tab.Remote.CanMoveInto(upload, "/home"), Is.True);
        Assert.That(_tab.Remote.CanMoveInto(_tab.Remote.Entries[0], "/"), Is.False, "the .. entry itself");
    }

    [Test]
    public async Task DroppingOnTheParentMovesUpOnThisComputer()
    {
        Directory.CreateDirectory(_local.File("sub"));
        File.WriteAllText(_local.File("sub/notes.txt"), "local");
        await _tab.Local.NavigateAsync(_local.File("sub"));

        await _tab.MoveAsync(_tab.Local, [_tab.Local.Entries.Single(entry => entry.Name == "notes.txt")], _tab.Local.Entries[0].FullPath);

        Assert.That(File.ReadAllText(_local.File("notes.txt")), Is.EqualTo("local"));
        Assert.That(_tab.Local.Entries.Select(entry => entry.Name), Is.EqualTo(new[] { ".." }));
    }

    [Test]
    public async Task DroppingOnADirectoryOfTheOtherPaneTransfersIntoIt()
    {
        File.WriteAllText(_local.File("notes.txt"), "uploaded");
        await _tab.UploadAsync([_local.File("notes.txt")], "/home/tester/upload");
        await Finished(_tab.Transfers.Single());
        Assert.That(_server.Content("/home/tester/upload/notes.txt"), Is.EqualTo("uploaded"));

        Directory.CreateDirectory(_local.File("in"));
        await _tab.DownloadAsync([_tab.Remote.Entries.Single(entry => entry.Name == "readme.txt")], _local.File("in"));
        await Finished(_tab.Transfers.Last());
        Assert.That(File.ReadAllText(_local.File("in/readme.txt")), Is.EqualTo("hello"));
    }

    [Test]
    public async Task TheParentEntryIsNeverTransferredRenamedOrDeleted()
    {
        _tab.Remote.SelectedEntries.Add(_tab.Remote.Entries[0]);
        Assert.That(_tab.DownloadCommand.CanExecute(null) || _tab.RenameCommand.CanExecute(null) || _tab.DeleteCommand.CanExecute(null), Is.False);

        _tab.Remote.SelectedEntries.Add(_tab.Remote.Entries.Single(entry => entry.Name == "readme.txt"));
        await _tab.DeleteCommand.ExecuteAsync(null);

        Assert.That(_dialogs.Confirmations.Single(), Does.Contain("readme.txt"));
        Assert.That(await _server.ExistsAsync("/home/tester/readme.txt", CancellationToken.None), Is.False);
        Assert.That(await _server.ExistsAsync("/home/tester", CancellationToken.None), Is.True);
    }

    [Test]
    public async Task AServerThatCannotBeTrustedLeavesTheTabWithTheReasonAndReconnect()
    {
        FileTransferViewModel tab = Tab(_ => throw new ServerNotTrustedException("The host key of files.example has changed."));

        await tab.ConnectAsync();

        Assert.That(tab.State, Is.EqualTo(FileTransferState.Failed));
        Assert.That(tab.StatusText, Does.Contain("has changed"));
        Assert.That(tab.ReconnectCommand.CanExecute(null), Is.True);
        tab.Dispose();
    }

    [Test]
    public async Task PlainFtpIsShownAsUnencrypted()
    {
        FileTransferViewModel tab = Tab(_ => Task.FromResult<IRemoteFileSystem>(new UnencryptedServer(_server)));

        await tab.ConnectAsync();

        Assert.That((tab.IsUnencrypted, tab.StatusText), Is.EqualTo((true, UiText.UnencryptedWarning)));
        tab.Dispose();
    }

    [Test]
    public void DisconnectingKeepsTheTabForReconnect()
    {
        _tab.Disconnect();

        Assert.That((_tab.State, _tab.HasEnded), Is.EqualTo((FileTransferState.Disconnected, true)));
        Assert.That(_connection.OpenConnections.Count, Is.Zero);
        Assert.That(_tab.Remote.Entries, Is.Empty);
    }

    private FileTransferViewModel Tab(Func<CancellationToken, Task<IRemoteFileSystem>> connect, string? startDirectory = null) =>
        new(_connection, (_, _) => Task.FromResult(new FileTransferTarget(startDirectory, [], (_, _, token) => connect(token))),
            _dialogs, new FileTransferPrompts(_dialogs, new CertificatePins(new AppSettings(), () => { })), action => action());

    private static async Task Finished(TransferItemViewModel transfer)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!transfer.IsFinished && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.That(transfer.IsFinished, Is.True);
        await Task.Delay(50); // the refresh after the transfer
    }

    private sealed class UnencryptedServer(IRemoteFileSystem inner) : IRemoteFileSystem
    {
        public string Description => "ftp://tester@files.example";
        public bool IsEncrypted => false;
        public Task<string> GetHomeDirectoryAsync(CancellationToken cancellationToken) => inner.GetHomeDirectoryAsync(cancellationToken);
        public Task<IReadOnlyList<RemoteEntry>> ListAsync(string directory, CancellationToken cancellationToken) => inner.ListAsync(directory, cancellationToken);
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken) => inner.ExistsAsync(path, cancellationToken);
        public Task DownloadAsync(string path, Stream destination, IProgress<long> bytes, CancellationToken cancellationToken) =>
            inner.DownloadAsync(path, destination, bytes, cancellationToken);
        public Task UploadAsync(Stream source, string path, IProgress<long> bytes, CancellationToken cancellationToken) =>
            inner.UploadAsync(source, path, bytes, cancellationToken);
        public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken) => inner.CreateDirectoryAsync(path, cancellationToken);
        public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken) => inner.RenameAsync(path, newPath, cancellationToken);
        public Task DeleteAsync(RemoteEntry entry, CancellationToken cancellationToken) => inner.DeleteAsync(entry, cancellationToken);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public class FileTransferPromptsTests
{
    private static readonly CertificateQuestion Question = new("ftp.example", 21, SelfSigned(), "RemoteCertificateChainErrors");

    [Test]
    public void AHostKeyQuestionShowsTheFingerprint()
    {
        FakeDialogService dialogs = new() { TrustAnswer = TrustDecision.TrustOnce };
        FileTransferPrompts prompts = new(dialogs, new CertificatePins(new AppSettings(), () => { }));

        TrustDecision decision = prompts.ConfirmHostKey(new HostKeyQuestion("files.example", 2222, "ssh-ed25519", "SHA256:abc", []));

        Assert.That(decision, Is.EqualTo(TrustDecision.TrustOnce));
        Assert.That(dialogs.TrustQuestions.Single(), Does.Contain("[files.example]:2222").And.Contain("SHA256:abc"));
    }

    [Test]
    public void ARememberedCertificateIsNotAskedAboutAgain()
    {
        AppSettings settings = new();
        int saves = 0;
        FakeDialogService dialogs = new() { TrustAnswer = TrustDecision.TrustAndRemember };
        FileTransferPrompts prompts = new(dialogs, new CertificatePins(settings, () => saves++));

        Assert.That(prompts.ConfirmCertificate(Question), Is.EqualTo(TrustDecision.TrustAndRemember));
        Assert.That(prompts.ConfirmCertificate(Question), Is.EqualTo(TrustDecision.TrustOnce));

        Assert.That(dialogs.TrustQuestions, Has.Count.EqualTo(1));
        Assert.That((settings.TrustedCertificates["ftp.example:21"], saves), Is.EqualTo((Question.Fingerprint, 1)));
    }

    private static System.Security.Cryptography.X509Certificates.X509Certificate2 SelfSigned()
    {
        using System.Security.Cryptography.RSA key = System.Security.Cryptography.RSA.Create(2048);
        System.Security.Cryptography.X509Certificates.CertificateRequest request = new("CN=ftp.example", key,
            System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(30));
    }
}

public class FileTransferConnectorTests
{
    private readonly FileTransferConnector _connector = new(new SessionPrograms { Find = _ => null });

    [Test]
    public void AnFtpPasswordWithALineBreakCannotAddCommands()
    {
        ConnectionInfo connection = new() { Protocol = ProtocolType.FTP, Hostname = "ftp.example", Username = "u", Password = "pw\r\nDELE x" };

        Assert.ThrowsAsync<SessionException>(() => _connector.PrepareAsync(connection, CancellationToken.None));
    }

    [Test]
    public async Task TheStartDirectoryComesFromTheConnection()
    {
        ConnectionInfo connection = new() { Protocol = ProtocolType.SFTP, Hostname = "web1", Port = 22 };
        connection.SetPluginProperty(FileTransferSettings.StartDirectoryKey, "/srv/www");

        FileTransferTarget target = await _connector.PrepareAsync(connection, CancellationToken.None);

        Assert.That(target.StartDirectory, Is.EqualTo("/srv/www"));
    }

    [Test]
    public void AnSftpHostThatCouldBeAnOptionIsRefused()
    {
        ConnectionInfo connection = new() { Protocol = ProtocolType.SFTP, Hostname = "-oProxyCommand=x" };

        Assert.ThrowsAsync<SessionException>(() => _connector.PrepareAsync(connection, CancellationToken.None));
    }
}

public class FileTransferPropertiesTests
{
    [Test]
    public void FtpConnectionsShowTheirFileTransferSettingsAndImplicitTlsMovesThePort()
    {
        ConnectionInfo connection = new() { Protocol = ProtocolType.FTP, Hostname = "ftp.example", Port = 21 };
        PropertyPanelViewModel panel = new() { Connection = connection };

        PropertyItemViewModel encryption = Item(panel, FileTransferSettings.EncryptionKey);
        Assert.That(encryption.Choices.Select(choice => choice.DisplayText), Does.Contain(UiText.FtpImplicitTls));
        Assert.That(Item(panel, FileTransferSettings.PassiveKey).BooleanValue, Is.True);
        Assert.That(panel.Groups.Single(group => group.Items.Contains(encryption)).Category, Is.EqualTo(UiText.FileTransferCategory));

        encryption.SelectedChoice = encryption.Choices.Single(choice => (FtpEncryption)choice.Value == FtpEncryption.Implicit);

        Assert.That(connection.GetPluginProperty(FileTransferSettings.EncryptionKey), Is.EqualTo("Implicit"));
        Assert.That(connection.Port, Is.EqualTo(990));
    }

    [Test]
    public void ADefaultValueIsNotStored()
    {
        ConnectionInfo connection = new() { Protocol = ProtocolType.FTP, Hostname = "ftp.example", Port = 21 };
        connection.SetPluginProperty(FileTransferSettings.PassiveKey, "False");
        PropertyPanelViewModel panel = new() { Connection = connection };

        Item(panel, FileTransferSettings.PassiveKey).BooleanValue = true;

        Assert.That(connection.TryGetPluginProperty(FileTransferSettings.PassiveKey, out _), Is.False);
    }

    [Test]
    public void SftpOnlyHasAStartDirectoryAndSshNone()
    {
        Assert.That(Names(new ConnectionInfo { Protocol = ProtocolType.SFTP }), Does.Contain(FileTransferSettings.StartDirectoryKey)
                                                                            .And.Not.Contain(FileTransferSettings.EncryptionKey));
        Assert.That(Names(new ConnectionInfo { Protocol = ProtocolType.SSH2 }), Has.None.StartsWith("mRx."));
    }

    private static IEnumerable<string> Names(ConnectionInfo connection) =>
        new PropertyPanelViewModel { Connection = connection }.Groups.SelectMany(group => group.Items).Select(item => item.Name);

    private static PropertyItemViewModel Item(PropertyPanelViewModel panel, string name) =>
        panel.Groups.SelectMany(group => group.Items).Single(item => item.Name == name);
}
