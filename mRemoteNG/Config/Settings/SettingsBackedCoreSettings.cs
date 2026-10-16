using System.Runtime.Versioning;

namespace mRemoteNG.Config.Settings
{
    /// <summary>Reads (and writes) <see cref="ICoreSettings"/> straight through to Properties.Settings.</summary>
    [SupportedOSPlatform("windows")]
    public class SettingsBackedCoreSettings : ICoreSettings
    {
        public bool ConDefaultAutomaticResize => Properties.Settings.Default.ConDefaultAutomaticResize;
        public bool ConDefaultCacheBitmaps => Properties.Settings.Default.ConDefaultCacheBitmaps;
        public string ConDefaultColors => Properties.Settings.Default.ConDefaultColors;
        public string ConDefaultDescription => Properties.Settings.Default.ConDefaultDescription;
        public bool ConDefaultDisableCursorBlinking => Properties.Settings.Default.ConDefaultDisableCursorBlinking;
        public bool ConDefaultDisableCursorShadow => Properties.Settings.Default.ConDefaultDisableCursorShadow;
        public bool ConDefaultDisableFullWindowDrag => Properties.Settings.Default.ConDefaultDisableFullWindowDrag;
        public bool ConDefaultDisableMenuAnimations => Properties.Settings.Default.ConDefaultDisableMenuAnimations;
        public bool ConDefaultDisplayThemes => Properties.Settings.Default.ConDefaultDisplayThemes;
        public bool ConDefaultDisplayWallpaper => Properties.Settings.Default.ConDefaultDisplayWallpaper;
        public string ConDefaultEC2Region => Properties.Settings.Default.ConDefaultEC2Region;
        public bool ConDefaultEnableDesktopComposition => Properties.Settings.Default.ConDefaultEnableDesktopComposition;
        public bool ConDefaultEnableFontSmoothing => Properties.Settings.Default.ConDefaultEnableFontSmoothing;
        public string ConDefaultEnvironmentTags => Properties.Settings.Default.ConDefaultEnvironmentTags;
        public string ConDefaultExtApp => Properties.Settings.Default.ConDefaultExtApp;
        public string ConDefaultExternalAddressProvider => Properties.Settings.Default.ConDefaultExternalAddressProvider;
        public string ConDefaultExternalCredentialProvider => Properties.Settings.Default.ConDefaultExternalCredentialProvider;
        public bool ConDefaultFavorite => Properties.Settings.Default.ConDefaultFavorite;
        public string ConDefaultIcon => Properties.Settings.Default.ConDefaultIcon;
        public string ConDefaultLoadBalanceInfo => Properties.Settings.Default.ConDefaultLoadBalanceInfo;
        public string ConDefaultMacAddress => Properties.Settings.Default.ConDefaultMacAddress;
        public string ConDefaultPostExtApp => Properties.Settings.Default.ConDefaultPostExtApp;
        public string ConDefaultPreExtApp => Properties.Settings.Default.ConDefaultPreExtApp;
        public string ConDefaultProtocol => Properties.Settings.Default.ConDefaultProtocol;
        public string ConDefaultPuttySession => Properties.Settings.Default.ConDefaultPuttySession;
        public string ConDefaultRDGatewayAccessToken => Properties.Settings.Default.ConDefaultRDGatewayAccessToken;
        public string ConDefaultRDGatewayDomain => Properties.Settings.Default.ConDefaultRDGatewayDomain;
        public string ConDefaultRDGatewayExternalCredentialProvider => Properties.Settings.Default.ConDefaultRDGatewayExternalCredentialProvider;
        public string ConDefaultRDGatewayHostname => Properties.Settings.Default.ConDefaultRDGatewayHostname;
        public string ConDefaultRDGatewayPassword => Properties.Settings.Default.ConDefaultRDGatewayPassword;
        public string ConDefaultRDGatewayUsageMethod => Properties.Settings.Default.ConDefaultRDGatewayUsageMethod;
        public string ConDefaultRDGatewayUseConnectionCredentials => Properties.Settings.Default.ConDefaultRDGatewayUseConnectionCredentials;
        public string ConDefaultRDGatewayUserViaAPI => Properties.Settings.Default.ConDefaultRDGatewayUserViaAPI;
        public string ConDefaultRDGatewayUsername => Properties.Settings.Default.ConDefaultRDGatewayUsername;
        public bool ConDefaultRDPAlertIdleTimeout => Properties.Settings.Default.ConDefaultRDPAlertIdleTimeout;
        public string ConDefaultRDPAuthenticationLevel => Properties.Settings.Default.ConDefaultRDPAuthenticationLevel;
        public int ConDefaultRDPMinutesToIdleTimeout => Properties.Settings.Default.ConDefaultRDPMinutesToIdleTimeout;
        public string ConDefaultRDPStartProgram => Properties.Settings.Default.ConDefaultRDPStartProgram;
        public string ConDefaultRDPStartProgramWorkDir => Properties.Settings.Default.ConDefaultRDPStartProgramWorkDir;
        public bool ConDefaultRedirectAudioCapture => Properties.Settings.Default.ConDefaultRedirectAudioCapture;
        public bool ConDefaultRedirectClipboard => Properties.Settings.Default.ConDefaultRedirectClipboard;
        public bool ConDefaultRedirectDiskDrives => Properties.Settings.Default.ConDefaultRedirectDiskDrives;
        public string ConDefaultRedirectDiskDrivesCustom => Properties.Settings.Default.ConDefaultRedirectDiskDrivesCustom;
        public bool ConDefaultRedirectKeys => Properties.Settings.Default.ConDefaultRedirectKeys;
        public bool ConDefaultRedirectPorts => Properties.Settings.Default.ConDefaultRedirectPorts;
        public bool ConDefaultRedirectPrinters => Properties.Settings.Default.ConDefaultRedirectPrinters;
        public bool ConDefaultRedirectSmartCards => Properties.Settings.Default.ConDefaultRedirectSmartCards;
        public string ConDefaultRedirectSound => Properties.Settings.Default.ConDefaultRedirectSound;
        public string ConDefaultRenderingEngine => Properties.Settings.Default.ConDefaultRenderingEngine;
        public string ConDefaultResolution => Properties.Settings.Default.ConDefaultResolution;
        public string ConDefaultSoundQuality => Properties.Settings.Default.ConDefaultSoundQuality;
        public string ConDefaultTerminalBackend => Properties.Settings.Default.ConDefaultTerminalBackend;
        public bool ConDefaultUseConsoleSession => Properties.Settings.Default.ConDefaultUseConsoleSession;
        public bool ConDefaultUseCredSsp => Properties.Settings.Default.ConDefaultUseCredSsp;
        public bool ConDefaultUseEnhancedMode => Properties.Settings.Default.ConDefaultUseEnhancedMode;
        public bool ConDefaultUseRCG => Properties.Settings.Default.ConDefaultUseRCG;
        public bool ConDefaultUseRedirectionServerName => Properties.Settings.Default.ConDefaultUseRedirectionServerName;
        public bool ConDefaultUseRestrictedAdmin => Properties.Settings.Default.ConDefaultUseRestrictedAdmin;
        public bool ConDefaultUseVmId => Properties.Settings.Default.ConDefaultUseVmId;
        public string ConDefaultUserField => Properties.Settings.Default.ConDefaultUserField;
        public string ConDefaultVNCAuthMode => Properties.Settings.Default.ConDefaultVNCAuthMode;
        public string ConDefaultVNCColors => Properties.Settings.Default.ConDefaultVNCColors;
        public string ConDefaultVNCCompression => Properties.Settings.Default.ConDefaultVNCCompression;
        public string ConDefaultVNCEncoding => Properties.Settings.Default.ConDefaultVNCEncoding;
        public string ConDefaultVNCProxyIP => Properties.Settings.Default.ConDefaultVNCProxyIP;
        public string ConDefaultVNCProxyPassword => Properties.Settings.Default.ConDefaultVNCProxyPassword;
        public int ConDefaultVNCProxyPort => Properties.Settings.Default.ConDefaultVNCProxyPort;
        public string ConDefaultVNCProxyType => Properties.Settings.Default.ConDefaultVNCProxyType;
        public string ConDefaultVNCProxyUsername => Properties.Settings.Default.ConDefaultVNCProxyUsername;
        public string ConDefaultVNCSmartSizeMode => Properties.Settings.Default.ConDefaultVNCSmartSizeMode;
        public bool ConDefaultVNCViewOnly => Properties.Settings.Default.ConDefaultVNCViewOnly;
        public bool DoNotTrimUsername => Properties.Settings.Default.DoNotTrimUsername;
        public string OpeningCommand => Properties.Settings.Default.OpeningCommand;
        public string PuttySavedSessionsPanel
        {
            get => Properties.Settings.Default.PuttySavedSessionsPanel;
            set => Properties.Settings.Default.PuttySavedSessionsPanel = value;
        }
        public bool SetHostnameLikeDisplayName => Properties.Settings.Default.SetHostnameLikeDisplayName;
    }
}
