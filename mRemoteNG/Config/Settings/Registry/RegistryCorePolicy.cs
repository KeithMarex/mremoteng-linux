using System.Runtime.Versioning;

namespace mRemoteNG.Config.Settings.Registry
{
    /// <summary>Reads <see cref="ICorePolicy"/> from the registry policy (see <see cref="CommonRegistrySettings"/>).</summary>
    [SupportedOSPlatform("windows")]
    public class RegistryCorePolicy : ICorePolicy
    {
        public bool AllowSaveUsernames => CommonRegistrySettings.AllowSaveUsernames;
        public bool AllowSavePasswords => CommonRegistrySettings.AllowSavePasswords;
    }
}
