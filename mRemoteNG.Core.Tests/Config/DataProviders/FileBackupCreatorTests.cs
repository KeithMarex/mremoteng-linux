using System.IO;
using mRemoteNG.Config.DataProviders;
using mRemoteNGTests.TestHelpers;
using NUnit.Framework;
using System;

namespace mRemoteNGTests.Config.DataProviders;

public class FileBackupCreatorTests
{
    private FileBackupCreator _fileBackupCreator;
    private string _testFilePath;
    private string _testFilePathBackup;
    private string _testFileDirectory;
    private string _testFileRollingBackup;

    [SetUp]
    public void Setup()
    {
        _testFilePath = FileTestHelpers.NewTempFilePath();
        _testFileDirectory = Path.GetDirectoryName(_testFilePath);
        _testFileRollingBackup = Path.GetFileName(_testFilePath) + ".*-*.backup";
        _testFilePathBackup = _testFilePath + ".backup";
        _fileBackupCreator = new FileBackupCreator();
    }

    [TearDown]
    public void Teardown()
    {
        if (Directory.Exists(_testFileDirectory))
            Directory.Delete(_testFileDirectory, true);
    }

    [Test]
    public void BackupCreatedWhenFileAlreadyExists()
    {
        File.WriteAllText(_testFilePath, "");
        _fileBackupCreator.CreateBackupFile(_testFilePath);
        var rollingBackupFiles = Directory.GetFiles(_testFileDirectory, _testFileRollingBackup);
        Assert.That(rollingBackupFiles.Length, Is.EqualTo(1));
    }

    [Test]
    public void BackupNotCreatedIfFileDidntAlreadyExist()
    {
        _fileBackupCreator.CreateBackupFile(_testFilePath);
        var backupFileExists = File.Exists(_testFilePathBackup);
        Assert.That(backupFileExists, Is.False);
    }

    [Test]
    public void CreateBackupFile_WithPathTraversal_ThrowsArgumentException()
    {
        string maliciousPath = ExistingFileReachedThrough(@"\..\");
        Assert.Throws<ArgumentException>(() => _fileBackupCreator.CreateBackupFile(maliciousPath));
    }

    [Test]
    public void CreateBackupFile_WithForwardSlashTraversal_ThrowsArgumentException()
    {
        string maliciousPath = ExistingFileReachedThrough("/../");
        Assert.Throws<ArgumentException>(() => _fileBackupCreator.CreateBackupFile(maliciousPath));
    }

    /// <summary>
    /// CreateBackupFile only validates the path of a file that exists (otherwise there is nothing to back
    /// up), so the traversal tests need a real file: returns "&lt;dir&gt;/sub{traversal}target.xml" and makes
    /// sure it exists. On Windows both separators resolve to &lt;dir&gt;/target.xml; on Linux a backslash is
    /// an ordinary character, so that file is created under the literal name.
    /// </summary>
    private string ExistingFileReachedThrough(string traversal)
    {
        string subDirectory = Path.Combine(_testFileDirectory, "sub");
        Directory.CreateDirectory(subDirectory);
        File.WriteAllText(Path.Combine(_testFileDirectory, "target.xml"), "");
        string path = subDirectory + traversal + "target.xml";
        if (!File.Exists(path))
            File.WriteAllText(path, "");
        return path;
    }
}