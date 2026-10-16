using System;

namespace mRemoteNG.Config.Serializers
{
    /// <summary>The connection file was written by a newer version than this one can read.</summary>
    public class UnsupportedConnectionFileVersionException(double fileVersion, double highestSupportedVersion)
        : Exception($"Incompatible connection file format (file format version {fileVersion}).")
    {
        public double FileVersion { get; } = fileVersion;

        public double HighestSupportedVersion { get; } = highestSupportedVersion;
    }
}
