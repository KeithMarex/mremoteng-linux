namespace mRemoteNG.Config.Settings
{
    /// <summary>
    /// The application settings that platform-neutral code reads. Each app supplies an
    /// implementation through <see cref="mRemoteNG.App.CoreRuntime.Settings"/>; the names match
    /// the WinForms app's Properties.Settings (and its options-page settings classes) so both apps
    /// share one meaning per setting.
    /// </summary>
    public interface ICoreSettings
    {
        int BackupFileKeepCount { get; }
        string BackupFileNameFormat { get; }
        bool ConDefaultAutomaticResize { get; }
        bool ConDefaultCacheBitmaps { get; }
        string ConDefaultColors { get; }
        string ConDefaultDescription { get; }
        bool ConDefaultDisableCursorBlinking { get; }
        bool ConDefaultDisableCursorShadow { get; }
        bool ConDefaultDisableFullWindowDrag { get; }
        bool ConDefaultDisableMenuAnimations { get; }
        bool ConDefaultDisplayThemes { get; }
        bool ConDefaultDisplayWallpaper { get; }
        string ConDefaultEC2Region { get; }
        bool ConDefaultEnableDesktopComposition { get; }
        bool ConDefaultEnableFontSmoothing { get; }
        string ConDefaultEnvironmentTags { get; }
        string ConDefaultExtApp { get; }
        string ConDefaultExternalAddressProvider { get; }
        string ConDefaultExternalCredentialProvider { get; }
        bool ConDefaultFavorite { get; }
        string ConDefaultIcon { get; }
        string ConDefaultLoadBalanceInfo { get; }
        string ConDefaultMacAddress { get; }
        string ConDefaultPostExtApp { get; }
        string ConDefaultPreExtApp { get; }
        string ConDefaultProtocol { get; }
        string ConDefaultPuttySession { get; }
        string ConDefaultRDGatewayAccessToken { get; }
        string ConDefaultRDGatewayDomain { get; }
        string ConDefaultRDGatewayExternalCredentialProvider { get; }
        string ConDefaultRDGatewayHostname { get; }
        string ConDefaultRDGatewayPassword { get; }
        string ConDefaultRDGatewayUsageMethod { get; }
        string ConDefaultRDGatewayUseConnectionCredentials { get; }
        string ConDefaultRDGatewayUserViaAPI { get; }
        string ConDefaultRDGatewayUsername { get; }
        bool ConDefaultRDPAlertIdleTimeout { get; }
        string ConDefaultRDPAuthenticationLevel { get; }
        int ConDefaultRDPMinutesToIdleTimeout { get; }
        string ConDefaultRDPStartProgram { get; }
        string ConDefaultRDPStartProgramWorkDir { get; }
        bool ConDefaultRedirectAudioCapture { get; }
        bool ConDefaultRedirectClipboard { get; }
        bool ConDefaultRedirectDiskDrives { get; }
        string ConDefaultRedirectDiskDrivesCustom { get; }
        bool ConDefaultRedirectKeys { get; }
        bool ConDefaultRedirectPorts { get; }
        bool ConDefaultRedirectPrinters { get; }
        bool ConDefaultRedirectSmartCards { get; }
        string ConDefaultRedirectSound { get; }
        string ConDefaultRenderingEngine { get; }
        string ConDefaultResolution { get; }
        string ConDefaultSoundQuality { get; }
        string ConDefaultTerminalBackend { get; }
        bool ConDefaultUseConsoleSession { get; }
        bool ConDefaultUseCredSsp { get; }
        bool ConDefaultUseEnhancedMode { get; }
        bool ConDefaultUseRCG { get; }
        bool ConDefaultUseRedirectionServerName { get; }
        bool ConDefaultUseRestrictedAdmin { get; }
        bool ConDefaultUseVmId { get; }
        string ConDefaultUserField { get; }
        string ConDefaultVNCAuthMode { get; }
        string ConDefaultVNCColors { get; }
        string ConDefaultVNCCompression { get; }
        string ConDefaultVNCEncoding { get; }
        string ConDefaultVNCProxyIP { get; }
        string ConDefaultVNCProxyPassword { get; }
        int ConDefaultVNCProxyPort { get; }
        string ConDefaultVNCProxyType { get; }
        string ConDefaultVNCProxyUsername { get; }
        string ConDefaultVNCSmartSizeMode { get; }
        bool ConDefaultVNCViewOnly { get; }
        bool DoNotTrimUsername { get; }
        string OpeningCommand { get; }
        string PuttySavedSessionsPanel { get; set; }
        bool SetHostnameLikeDisplayName { get; }
    }
}
