using System;

namespace mRemoteNG.Config.Serializers
{
    /// <summary>A file to import is not in the format its importer expects.</summary>
    /// <remarks>Replaces System.IO.FileFormatException, which on .NET lives in WPF's WindowsBase.</remarks>
    public class ConnectionFileFormatException(string message) : FormatException(message);
}
