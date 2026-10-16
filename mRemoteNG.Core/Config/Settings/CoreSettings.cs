namespace mRemoteNG.Config.Settings
{
    /// <summary>
    /// Plain settings object with the same defaults as the WinForms app's Settings.settings.
    /// Used where no other store is configured (the Linux app, tests).
    /// </summary>
    public class CoreSettings : ICoreSettings
    {
        public int BackupFileKeepCount { get; set; } = 10;
        public string BackupFileNameFormat { get; set; } = "{0}.{1:yyyyMMdd-HHmmssffff}.backup";
        public bool ConDefaultAutomaticResize { get; set; } = true;
        public bool ConDefaultCacheBitmaps { get; set; } = false;
        public string ConDefaultColors { get; set; } = "Colors16Bit";
        public string ConDefaultDescription { get; set; } = "";
        public bool ConDefaultDisableCursorBlinking { get; set; } = false;
        public bool ConDefaultDisableCursorShadow { get; set; } = false;
        public bool ConDefaultDisableFullWindowDrag { get; set; } = false;
        public bool ConDefaultDisableMenuAnimations { get; set; } = false;
        public bool ConDefaultDisplayThemes { get; set; } = false;
        public bool ConDefaultDisplayWallpaper { get; set; } = false;
        public string ConDefaultEC2Region { get; set; } = "eu-central-1";
        public bool ConDefaultEnableDesktopComposition { get; set; } = false;
        public bool ConDefaultEnableFontSmoothing { get; set; } = false;
        public string ConDefaultEnvironmentTags { get; set; } = "";
        public string ConDefaultExtApp { get; set; } = "";
        public string ConDefaultExternalAddressProvider { get; set; } = "None";
        public string ConDefaultExternalCredentialProvider { get; set; } = "None";
        public bool ConDefaultFavorite { get; set; } = false;
        public string ConDefaultIcon { get; set; } = "mRemoteNG";
        public string ConDefaultLoadBalanceInfo { get; set; } = "";
        public string ConDefaultMacAddress { get; set; } = "";
        public string ConDefaultPostExtApp { get; set; } = "";
        public string ConDefaultPreExtApp { get; set; } = "";
        public string ConDefaultProtocol { get; set; } = "RDP";
        public string ConDefaultPuttySession { get; set; } = "Default Settings";
        public string ConDefaultRDGatewayAccessToken { get; set; } = "";
        public string ConDefaultRDGatewayDomain { get; set; } = "";
        public string ConDefaultRDGatewayExternalCredentialProvider { get; set; } = "None";
        public string ConDefaultRDGatewayHostname { get; set; } = "";
        public string ConDefaultRDGatewayPassword { get; set; } = "";
        public string ConDefaultRDGatewayUsageMethod { get; set; } = "Never";
        public string ConDefaultRDGatewayUseConnectionCredentials { get; set; } = "Yes";
        public string ConDefaultRDGatewayUserViaAPI { get; set; } = "";
        public string ConDefaultRDGatewayUsername { get; set; } = "";
        public bool ConDefaultRDPAlertIdleTimeout { get; set; } = false;
        public string ConDefaultRDPAuthenticationLevel { get; set; } = "NoAuth";
        public int ConDefaultRDPMinutesToIdleTimeout { get; set; } = 0;
        public string ConDefaultRDPStartProgram { get; set; } = "";
        public string ConDefaultRDPStartProgramWorkDir { get; set; } = "";
        public bool ConDefaultRedirectAudioCapture { get; set; } = false;
        public bool ConDefaultRedirectClipboard { get; set; } = false;
        public bool ConDefaultRedirectDiskDrives { get; set; } = false;
        public string ConDefaultRedirectDiskDrivesCustom { get; set; } = "";
        public bool ConDefaultRedirectKeys { get; set; } = false;
        public bool ConDefaultRedirectPorts { get; set; } = false;
        public bool ConDefaultRedirectPrinters { get; set; } = false;
        public bool ConDefaultRedirectSmartCards { get; set; } = false;
        public string ConDefaultRedirectSound { get; set; } = "DoNotPlay";
        public string ConDefaultRenderingEngine { get; set; } = "EdgeChromium";
        public string ConDefaultResolution { get; set; } = "SmartSize";
        public string ConDefaultSoundQuality { get; set; } = "Dynamic";
        public string ConDefaultTerminalBackend { get; set; } = "PuttyNg";
        public bool ConDefaultUseConsoleSession { get; set; } = false;
        public bool ConDefaultUseCredSsp { get; set; } = true;
        public bool ConDefaultUseEnhancedMode { get; set; } = false;
        public bool ConDefaultUseRCG { get; set; } = false;
        public bool ConDefaultUseRedirectionServerName { get; set; } = false;
        public bool ConDefaultUseRestrictedAdmin { get; set; } = false;
        public bool ConDefaultUseVmId { get; set; } = false;
        public string ConDefaultUserField { get; set; } = "";
        public string ConDefaultVNCAuthMode { get; set; } = "AuthVNC";
        public string ConDefaultVNCColors { get; set; } = "ColNormal";
        public string ConDefaultVNCCompression { get; set; } = "CompNone";
        public string ConDefaultVNCEncoding { get; set; } = "EncHextile";
        public string ConDefaultVNCProxyIP { get; set; } = "";
        public string ConDefaultVNCProxyPassword { get; set; } = "";
        public int ConDefaultVNCProxyPort { get; set; } = 0;
        public string ConDefaultVNCProxyType { get; set; } = "ProxyNone";
        public string ConDefaultVNCProxyUsername { get; set; } = "";
        public string ConDefaultVNCSmartSizeMode { get; set; } = "SmartSAspect";
        public bool ConDefaultVNCViewOnly { get; set; } = false;
        public bool DoNotTrimUsername { get; set; } = false;
        public string OpeningCommand { get; set; } = "";
        public string PuttySavedSessionsPanel { get; set; } = "";
        public bool SetHostnameLikeDisplayName { get; set; } = false;
    }
}
