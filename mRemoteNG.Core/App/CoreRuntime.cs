using System;
using mRemoteNG.Config.Settings;
using mRemoteNG.Messages;

namespace mRemoteNG.App
{
    /// <summary>
    /// Application-wide services needed by platform-neutral code. The WinForms app's
    /// <c>Runtime</c> forwards to these, so both share a single instance.
    /// </summary>
    public static class CoreRuntime
    {
        public static MessageCollector MessageCollector { get; } = new MessageCollector();

        /// <summary>
        /// The settings store. The WinForms app replaces it with one backed by Properties.Settings
        /// as soon as its assembly loads; until then (and on Linux, in tests) the defaults apply.
        /// </summary>
        public static ICoreSettings Settings { get; set; } = new CoreSettings();

        /// <summary>Administrator policy; the WinForms app installs its registry-backed policy at load.</summary>
        public static ICorePolicy Policy { get; set; } = new CorePolicy();

        /// <summary>Storing credentials in the credential manager is not enabled; passwords stay in the connection file.</summary>
        public static bool UseCredentialManager => false;

        /// <summary>
        /// Whether the current connection file loaded successfully. A failed load clears it, which stops
        /// the (empty) connection tree from being saved over the file.
        /// </summary>
        public static bool IsConnectionsFileLoaded { get; set; }

        /// <summary>
        /// Decrypts a password from an RDCMan (.rdg) file. RDCMan protects them with Windows DPAPI in machine
        /// scope, so only the WinForms app on that machine installs a real decryptor; elsewhere imported RDCMan
        /// passwords stay empty.
        /// </summary>
        public static Func<string, string> DecryptRdcManPassword { get; set; } = _ => string.Empty;
    }
}
