using System;
using System.IO;
using mRemoteNG.App;
using mRemoteNG.Messages;
using mRemoteNG.Resources.Language;
using mRemoteNG.Tools;

namespace mRemoteNG.Config.DataProviders
{
    public class FileBackupCreator
    {
        public void CreateBackupFile(string fileName)
        {
            try
            {
                if (WeDontNeedToBackup(fileName))
                    return;

                PathValidator.ValidatePathOrThrow(fileName, nameof(fileName));

                string backupFileName =
                    string.Format(CoreRuntime.Settings.BackupFileNameFormat, fileName, DateTime.Now);
                
                PathValidator.ValidatePathOrThrow(backupFileName, nameof(backupFileName));
                
                File.Copy(fileName, backupFileName);
            }
            catch (Exception ex)
            {
                CoreRuntime.MessageCollector.AddExceptionMessage(Language.ConnectionsFileBackupFailed, ex,
                                                             MessageClass.WarningMsg);
                throw;
            }
        }

        private bool WeDontNeedToBackup(string filePath)
        {
            return FeatureIsTurnedOff() || FileDoesntExist(filePath);
        }

        private bool FileDoesntExist(string filePath)
        {
            return !File.Exists(filePath);
        }

        private bool FeatureIsTurnedOff()
        {
            return CoreRuntime.Settings.BackupFileKeepCount == 0;
        }
    }
}