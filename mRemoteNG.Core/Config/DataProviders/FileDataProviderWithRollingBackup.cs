using System.Runtime.Versioning;

namespace mRemoteNG.Config.DataProviders
{
    public class FileDataProviderWithRollingBackup(string filePath) : FileDataProvider(filePath)
    {
        private readonly FileBackupCreator _fileBackupCreator = new FileBackupCreator();

        public override void Save(string content)
        {
            _fileBackupCreator.CreateBackupFile(FilePath);
            base.Save(content);
        }
    }
}