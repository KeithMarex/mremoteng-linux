using System.Security;
using mRemoteNG.App;
using mRemoteNG.Avalonia.Settings;
using mRemoteNG.Config.DataProviders;
using mRemoteNG.Config.Serializers.ConnectionSerializers;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Messages;
using mRemoteNG.Security;
using mRemoteNG.Security.Factories;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Avalonia.Services;

/// <summary>
/// Creates, opens and saves connection files (confCons.xml) with the same Core code as Windows mRemoteNG,
/// so the files stay interchangeable. FTP and SFTP connections, which Windows does not know, are also kept in a
/// sidecar file next to the connection file (see <see cref="ConnectionFileSidecar"/>).
/// </summary>
public sealed class ConnectionFileService(AppSettings settings)
{
    public ConnectionTreeModel? Model { get; private set; }

    public string? FilePath { get; private set; }

    public RootNodeInfo? RootNode => Model?.RootNodes.OfType<RootNodeInfo>().FirstOrDefault();

    /// <summary>Starts an empty connection file at <paramref name="filePath"/> and writes it.</summary>
    public ConnectionTreeModel CreateNew(string filePath)
    {
        ConnectionTreeModel model = new();
        model.AddRootNode(new RootNodeInfo(RootNodeType.Connection));
        SaveModel(model, filePath);

        Model = model;
        FilePath = filePath;
        CoreRuntime.IsConnectionsFileLoaded = true;
        return model;
    }

    /// <summary>
    /// Opens a connection file. <paramref name="passwordPrompt"/> is called (possibly repeatedly) when the file
    /// is password protected; it may run on a background thread.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is empty, or no correct password was given.</exception>
    public ConnectionTreeModel Open(string filePath, Func<Optional<SecureString>> passwordPrompt)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("The connection file does not exist.", filePath);

        bool wasLoaded = CoreRuntime.IsConnectionsFileLoaded;
        try
        {
            ConnectionTreeModel? model = new XmlConnectionsDeserializer(passwordPrompt).Deserialize(File.ReadAllText(filePath))
                                         ?? throw new InvalidDataException($"{filePath} could not be opened: it is empty or the password was not given.");
            ApplySidecar(filePath, model.GetRecursiveChildList());
            Model = model;
            FilePath = filePath;
            CoreRuntime.IsConnectionsFileLoaded = true;
            return model;
        }
        catch
        {
            // The previously open file stays open, so it may still be saved.
            CoreRuntime.IsConnectionsFileLoaded = wasLoaded;
            throw;
        }
    }

    /// <summary>Saves the open file in place, keeping rolling backups of the previous versions.</summary>
    public void Save()
    {
        SaveModel(RequireModel(), FilePath ?? throw new InvalidOperationException("No connection file is open."));
    }

    public void SaveAs(string filePath)
    {
        SaveModel(RequireModel(), filePath);
        FilePath = filePath;
    }

    private ConnectionTreeModel RequireModel()
    {
        if (Model is null)
            throw new InvalidOperationException("No connection file is open.");
        if (!CoreRuntime.IsConnectionsFileLoaded)
            throw new InvalidOperationException("The connection file did not load correctly; saving now would overwrite it.");
        return Model;
    }

    private void SaveModel(ConnectionTreeModel model, string filePath)
    {
        ICryptographyProvider cryptographyProvider =
            new CryptoProviderFactory(settings.EncryptionEngine, settings.EncryptionBlockCipherMode).Build();
        cryptographyProvider.KeyDerivationIterations = settings.EncryptionKeyDerivationIterations;

        string xml = new XmlConnectionSerializerFactory()
            .Build(cryptographyProvider, model, new SaveFilter(), settings.EncryptCompleteConnectionsFile,
                   Version.Parse(settings.ConnectionFileVersion))
            .Serialize(model.RootNodes.OfType<RootNodeInfo>().First());

        string fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        new FileBackupCreator().CreateBackupFile(fullPath);
        WriteAtomically(fullPath, xml);
        new FileBackupPruner().PruneBackupFiles(fullPath, CoreRuntime.Settings.BackupFileKeepCount);

        string sidecarPath = ConnectionFileSidecar.PathFor(fullPath);
        if (ConnectionFileSidecar.Build(model.GetRecursiveChildList()) is { } sidecar)
            WriteAtomically(sidecarPath, sidecar);
        else
            File.Delete(sidecarPath);
    }

    /// <summary>Restores FTP and SFTP connections from the sidecar of <paramref name="connectionFile"/>, if it has one.</summary>
    public static void ApplySidecar(string connectionFile, IEnumerable<Connection.ConnectionInfo> nodes)
    {
        string sidecarPath = ConnectionFileSidecar.PathFor(connectionFile);
        if (!File.Exists(sidecarPath))
            return;

        try
        {
            ConnectionFileSidecar.Apply(File.ReadAllText(sidecarPath), nodes);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            CoreRuntime.MessageCollector.AddExceptionMessage($"Could not read {sidecarPath}; FTP, SFTP and SPICE connections may show as RAW, SSH2 and VNC.",
                                                             ex, MessageClass.WarningMsg, logOnly: false);
        }
    }

    // Write next to the target and rename over it, so a crash or full disk never leaves a half-written
    // connection file behind (the file may be shared with Windows users).
    private static void WriteAtomically(string filePath, string content)
    {
        string temporaryFile = filePath + ".saving";
        File.WriteAllText(temporaryFile, content);
        File.Move(temporaryFile, filePath, overwrite: true);
    }
}
