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
    }
}
