using System.Collections.Generic;
using System.IO;
using mRemoteNG.Config.Serializers;

namespace mRemoteNG.Config.Import
{
    /// <summary>Picks the importer for a file from its extension; shared by the WinForms and Linux apps.</summary>
    public static class FileImporters
    {
        /// <summary>The file extensions <see cref="ForFile"/> understands, with what they contain.</summary>
        public static IReadOnlyDictionary<string, string> SupportedExtensions { get; } = new Dictionary<string, string>
        {
            [".xml"] = "mRemoteNG connection file",
            [".csv"] = "mRemoteNG CSV export",
            [".rdp"] = "Remote Desktop connection",
            [".rdg"] = "Remote Desktop Connection Manager",
            [".dat"] = "PuTTY Connection Manager",
            [".crt"] = "SecureCRT sessions"
        };

        // TODO: Use the file contents to determine the file type instead of trusting the extension
        public static IConnectionImporter<string> ForFile(string fileName)
        {
            string extension = Path.GetExtension(fileName) ?? "";
            return extension.ToLowerInvariant() switch
            {
                ".xml" => new MRemoteNGXmlImporter(),
                ".csv" => new MRemoteNGCsvImporter(),
                ".rdp" => new RemoteDesktopConnectionImporter(),
                ".rdg" => new RemoteDesktopConnectionManagerImporter(),
                ".dat" => new PuttyConnectionManagerImporter(),
                ".crt" => new SecureCRTImporter(),
                _ => throw new ConnectionFileFormatException("Unrecognized file format.")
            };
        }
    }
}
