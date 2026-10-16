using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using mRemoteNG.Config.Settings;
using mRemoteNG.Config.Settings.Registry;
using mRemoteNG.Security;

namespace mRemoteNG.App
{
    [SupportedOSPlatform("windows")]
    internal static class CoreRuntimeInitializer
    {
        /// <summary>
        /// Runs as soon as this assembly loads (app and mRemoteNGTests alike), before any Core code can
        /// read settings or policy: backs them with Properties.Settings and the registry policy.
        /// </summary>
        [ModuleInitializer]
        internal static void Initialize()
        {
            CoreRuntime.Settings = new SettingsBackedCoreSettings();
            CoreRuntime.Policy = new RegistryCorePolicy();
            CoreRuntime.DecryptRdcManPassword = RdcManPasswordDecryptor.Decrypt;
        }
    }
}
