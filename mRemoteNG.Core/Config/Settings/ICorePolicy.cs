namespace mRemoteNG.Config.Settings
{
    /// <summary>
    /// Administrator policy honoured by platform-neutral code. The WinForms app reads it from the
    /// HKLM\SOFTWARE\mRemoteNG registry policy; <see cref="CorePolicy"/> holds the defaults.
    /// </summary>
    public interface ICorePolicy
    {
        bool AllowSaveUsernames { get; }
        bool AllowSavePasswords { get; }
    }

    public class CorePolicy : ICorePolicy
    {
        public bool AllowSaveUsernames { get; set; } = true;
        public bool AllowSavePasswords { get; set; } = true;
    }
}
