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
    }
}
