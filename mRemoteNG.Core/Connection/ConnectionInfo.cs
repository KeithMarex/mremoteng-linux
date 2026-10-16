using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using mRemoteNG.App;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Connection.Protocol.Http;
using mRemoteNG.Connection.Protocol.Terminal;
using mRemoteNG.Connection.Protocol.RDP;
using mRemoteNG.Connection.Protocol.VNC;
using mRemoteNG.Container;
using mRemoteNG.Tools;
using mRemoteNG.Tree;
using mRemoteNG.Resources.Language;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Connection
{
    [DefaultProperty("Name")]
    [TypeDescriptionProvider(WinFormsPropertyGridTypes.ConnectionInfoTypeDescriptionProvider)]
    public class ConnectionInfo : AbstractConnectionRecord, IHasParent, IInheritable
    {
        private ConnectionInfoInheritance _inheritance;
        private readonly Dictionary<string, string> _pluginProperties = new(StringComparer.OrdinalIgnoreCase);

        #region Public Properties

        [Browsable(false)]
        public ConnectionInfoInheritance Inheritance
        {
            get => _inheritance;
            set => _inheritance = _inheritance.Parent != this
                ? _inheritance.Clone(this)
                : value;
        }

        [Browsable(false)] public ProtocolList OpenConnections { get; protected set; }

        [Browsable(false)] public virtual bool IsContainer { get; set; }

        [Browsable(false)] public bool IsDefault { get; set; }

        [Browsable(false)] public ContainerInfo Parent { get; internal set; }

        [Browsable(false)]
        public bool IsQuickConnect { get; set; }

        [Browsable(false)]
        public bool PleaseConnect { get; set; }

        [Browsable(false)]
        public IReadOnlyDictionary<string, string> PluginProperties => _pluginProperties;

        #endregion

        #region Constructors

        public ConnectionInfo()
            : this(Guid.NewGuid().ToString())
        {
        }

        public ConnectionInfo(string uniqueId)
            : base(uniqueId)
        {
            SetTreeDisplayDefaults();
            SetConnectionDefaults();
            SetProtocolDefaults();
            SetRemoteDesktopServicesDefaults();
            SetRdGatewayDefaults();
            SetAppearanceDefaults();
            SetRedirectDefaults();
            SetMiscDefaults();
            SetVncDefaults();
            SetNonBrowsablePropertiesDefaults();
            SetDefaults();
        }

        #endregion

        #region Public Methods

        public virtual ConnectionInfo Clone()
        {
            ConnectionInfo newConnectionInfo = new();
            newConnectionInfo.CopyFrom(this);
            return newConnectionInfo;
        }

        /// <summary>
        /// Copies all connection and inheritance values
        /// from the given <see cref="sourceConnectionInfo"/>.
        /// </summary>
        /// <param name="sourceConnectionInfo"></param>
        public void CopyFrom(ConnectionInfo sourceConnectionInfo)
        {
            IEnumerable<PropertyInfo> properties = GetType().BaseType?.GetProperties().Where(prop => prop.CanRead && prop.CanWrite);
            if (properties == null) return;
            foreach (PropertyInfo property in properties)
            {
                if (property.Name == nameof(Parent)) continue;
                object remotePropertyValue = property.GetValue(sourceConnectionInfo, null);
                property.SetValue(this, remotePropertyValue, null);
            }

            _pluginProperties.Clear();
            foreach (KeyValuePair<string, string> entry in sourceConnectionInfo.PluginProperties)
            {
                _pluginProperties[entry.Key] = entry.Value;
            }

            ConnectionInfoInheritance clonedInheritance = sourceConnectionInfo.Inheritance.Clone(this);
            Inheritance = clonedInheritance;
        }

        public string GetPluginProperty(string key, string defaultValue = "")
        {
            return _pluginProperties.TryGetValue(key, out string? value) ? value : defaultValue;
        }

        public bool TryGetPluginProperty(string key, out string value)
        {
            return _pluginProperties.TryGetValue(key, out value!);
        }

        public void SetPluginProperty(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Plugin property key cannot be null or whitespace.", nameof(key));
            }

            if (string.IsNullOrEmpty(value))
            {
                if (_pluginProperties.Remove(key))
                {
                    RaisePropertyChangedEvent(this, new PropertyChangedEventArgs(key));
                }

                return;
            }

            if (_pluginProperties.TryGetValue(key, out string? existingValue) &&
                string.Equals(existingValue, value, StringComparison.Ordinal))
            {
                return;
            }

            _pluginProperties[key] = value;
            RaisePropertyChangedEvent(this, new PropertyChangedEventArgs(key));
        }

        internal void ReplacePluginProperties(IEnumerable<KeyValuePair<string, string>> properties)
        {
            _pluginProperties.Clear();
            foreach (KeyValuePair<string, string> property in properties)
            {
                if (string.IsNullOrWhiteSpace(property.Key))
                {
                    continue;
                }

                _pluginProperties[property.Key] = property.Value ?? string.Empty;
            }
        }

        public virtual TreeNodeType GetTreeNodeType()
        {
            return TreeNodeType.Connection;
        }

        private void SetDefaults()
        {
            if (Port == 0)
            {
                SetDefaultPort();
            }
        }

        public int GetDefaultPort()
        {
            return GetDefaultPort(Protocol);
        }

        public void SetDefaultPort()
        {
            Port = GetDefaultPort();
        }

        protected virtual IEnumerable<PropertyInfo> GetProperties(string[] excludedPropertyNames)
        {
            PropertyInfo[] properties = typeof(ConnectionInfo).GetProperties();
            IEnumerable<PropertyInfo> filteredProperties = properties.Where((prop) => !excludedPropertyNames.Contains(prop.Name));
            return filteredProperties;
        }

        public virtual IEnumerable<PropertyInfo> GetSerializableProperties()
        {
            string[] excludedProperties = new[]
            {
                "Parent", "Name", "Hostname", "Port", "Inheritance", "OpenConnections",
                "IsContainer", "IsDefault", "PositionID", "ConstantID", "TreeNode", "IsQuickConnect", "PleaseConnect",
                nameof(PluginProperties) // read-only; persisted separately as PluginData
            };

            return GetProperties(excludedProperties);
        }

        public virtual void SetParent(ContainerInfo newParent)
        {
            RemoveParent();
            newParent?.AddChild(this);
        }

        public void RemoveParent()
        {
            Parent?.RemoveChild(this);
        }

        public ConnectionInfo GetRootParent()
        {
            return Parent != null ? Parent.GetRootParent() : this;
        }

        #endregion

        #region Public Enumerations

        [Flags()]
        public enum Force
        {
            None = 0,
            UseConsoleSession = 1,
            Fullscreen = 2,
            DoNotJump = 4,
            OverridePanel = 8,
            DontUseConsoleSession = 16,
            NoCredentials = 32,
            ViewOnly = 64
        }

        #endregion

        #region Private Methods

        protected override TPropertyType GetPropertyValue<TPropertyType>(string propertyName, TPropertyType value)
        {
            if (!ShouldThisPropertyBeInherited(propertyName))
                return value;

            bool couldGetInheritedValue =
                TryGetInheritedPropertyValue<TPropertyType>(propertyName, out TPropertyType inheritedValue);

            return couldGetInheritedValue
                ? inheritedValue
                : value;
        }

        private bool ShouldThisPropertyBeInherited(string propertyName)
        {
            return
                Inheritance.InheritanceActive &&
                ParentIsValidInheritanceTarget() &&
                IsInheritanceTurnedOnForThisProperty(propertyName);
        }

        private bool ParentIsValidInheritanceTarget()
        {
            return Parent != null;
        }

        private bool IsInheritanceTurnedOnForThisProperty(string propertyName)
        {
            Type inheritType = Inheritance.GetType();
            PropertyInfo inheritPropertyInfo = inheritType.GetProperty(propertyName);
            bool inheritPropertyValue = inheritPropertyInfo != null && Convert.ToBoolean(inheritPropertyInfo.GetValue(Inheritance, null));
            return inheritPropertyValue;
        }

        private bool TryGetInheritedPropertyValue<TPropertyType>(string propertyName, out TPropertyType inheritedValue)
        {
            try
            {
                Type connectionInfoType = Parent.GetType();
                PropertyInfo parentPropertyInfo = connectionInfoType.GetProperty(propertyName);
                if (parentPropertyInfo == null)
                    throw new NullReferenceException(
                        $"Could not retrieve property data for property '{propertyName}' on parent node '{Parent?.Name}'"
                    );

                inheritedValue = (TPropertyType)parentPropertyInfo.GetValue(Parent, null);
                return true;
            }
            catch (Exception e)
            {
                CoreRuntime.MessageCollector.AddExceptionStackTrace($"Error retrieving inherited property '{propertyName}'", e);
                inheritedValue = default(TPropertyType);
                return false;
            }
        }

        private static int GetDefaultPort(ProtocolType protocol)
        {
            return DefaultPorts.For(protocol);
        }

        private void SetTreeDisplayDefaults()
        {
            Name = Language.NewConnection;
            Description = CoreRuntime.Settings.ConDefaultDescription;
            Icon = CoreRuntime.Settings.ConDefaultIcon;
            Panel = Language.General;
            Color = string.Empty;
            TabColor = string.Empty;
            ConnectionFrameColor = ConnectionFrameColor.None;
        }

        private void SetConnectionDefaults()
        {
            Hostname = string.Empty;
            ExternalAddressProvider = (ExternalAddressProvider)Enum.Parse(typeof(ExternalAddressProvider), CoreRuntime.Settings.ConDefaultExternalAddressProvider);
            EC2Region = CoreRuntime.Settings.ConDefaultEC2Region;
            ExternalCredentialProvider = (ExternalCredentialProvider)Enum.Parse(typeof(ExternalCredentialProvider), CoreRuntime.Settings.ConDefaultExternalCredentialProvider);
            UserViaAPI = "";
        }

        private void SetProtocolDefaults()
        {
            Protocol = (ProtocolType)Enum.Parse(typeof(ProtocolType), CoreRuntime.Settings.ConDefaultProtocol);
            ExtApp = CoreRuntime.Settings.ConDefaultExtApp;
            Port = 0;
            PuttySession = CoreRuntime.Settings.ConDefaultPuttySession;
            UseConsoleSession = CoreRuntime.Settings.ConDefaultUseConsoleSession;
            RDPAuthenticationLevel = (AuthenticationLevel)Enum.Parse(typeof(AuthenticationLevel), CoreRuntime.Settings.ConDefaultRDPAuthenticationLevel);
            RDPMinutesToIdleTimeout = CoreRuntime.Settings.ConDefaultRDPMinutesToIdleTimeout;
            RDPAlertIdleTimeout = CoreRuntime.Settings.ConDefaultRDPAlertIdleTimeout;
            LoadBalanceInfo = CoreRuntime.Settings.ConDefaultLoadBalanceInfo;
            RenderingEngine = (RenderingEngine)Enum.Parse(typeof(RenderingEngine), CoreRuntime.Settings.ConDefaultRenderingEngine);
            TerminalBackend = (TerminalBackend)Enum.Parse(typeof(TerminalBackend), CoreRuntime.Settings.ConDefaultTerminalBackend);
            UseCredSsp = CoreRuntime.Settings.ConDefaultUseCredSsp;
            UseRestrictedAdmin = CoreRuntime.Settings.ConDefaultUseRestrictedAdmin;
            UseRCG = CoreRuntime.Settings.ConDefaultUseRCG;
            UseRedirectionServerName = CoreRuntime.Settings.ConDefaultUseRedirectionServerName;
            UseVmId = CoreRuntime.Settings.ConDefaultUseVmId;
            UseEnhancedMode = CoreRuntime.Settings.ConDefaultUseEnhancedMode;
        }

        private void SetRemoteDesktopServicesDefaults()
        {
            RDPStartProgram = string.Empty;
            RDPStartProgramWorkDir = string.Empty;
        }

        private void SetRdGatewayDefaults()
        {
            RDGatewayUsageMethod = (RDGatewayUsageMethod)Enum.Parse(typeof(RDGatewayUsageMethod), CoreRuntime.Settings.ConDefaultRDGatewayUsageMethod);
            RDGatewayHostname = CoreRuntime.Settings.ConDefaultRDGatewayHostname;
            RDGatewayUseConnectionCredentials = (RDGatewayUseConnectionCredentials)Enum.Parse(typeof(RDGatewayUseConnectionCredentials), CoreRuntime.Settings.ConDefaultRDGatewayUseConnectionCredentials);
            RDGatewayUsername = CoreRuntime.Settings.ConDefaultRDGatewayUsername;
            RDGatewayPassword = CoreRuntime.Settings.ConDefaultRDGatewayPassword;
            RDGatewayDomain = CoreRuntime.Settings.ConDefaultRDGatewayDomain;
            RDGatewayAccessToken = CoreRuntime.Settings.ConDefaultRDGatewayAccessToken;
            RDGatewayExternalCredentialProvider = (ExternalCredentialProvider)Enum.Parse(typeof(ExternalCredentialProvider), CoreRuntime.Settings.ConDefaultRDGatewayExternalCredentialProvider);
            RDGatewayUserViaAPI = CoreRuntime.Settings.ConDefaultRDGatewayUserViaAPI;
        }

        private void SetAppearanceDefaults()
        {
            Resolution = Enum.TryParse(CoreRuntime.Settings.ConDefaultResolution, out RDPResolutions res)
                ? res
                : RDPResolutions.SmartSize;
            AutomaticResize = CoreRuntime.Settings.ConDefaultAutomaticResize;
            Colors = (RDPColors)Enum.Parse(typeof(RDPColors), CoreRuntime.Settings.ConDefaultColors);
            CacheBitmaps = CoreRuntime.Settings.ConDefaultCacheBitmaps;
            DisplayWallpaper = CoreRuntime.Settings.ConDefaultDisplayWallpaper;
            DisplayThemes = CoreRuntime.Settings.ConDefaultDisplayThemes;
            EnableFontSmoothing = CoreRuntime.Settings.ConDefaultEnableFontSmoothing;
            EnableDesktopComposition = CoreRuntime.Settings.ConDefaultEnableDesktopComposition;
            DisableFullWindowDrag = CoreRuntime.Settings.ConDefaultDisableFullWindowDrag;
            DisableMenuAnimations = CoreRuntime.Settings.ConDefaultDisableMenuAnimations;
            DisableCursorShadow = CoreRuntime.Settings.ConDefaultDisableCursorShadow;
            DisableCursorBlinking = CoreRuntime.Settings.ConDefaultDisableCursorBlinking;
        }

        private void SetRedirectDefaults()
        {
            RedirectKeys = CoreRuntime.Settings.ConDefaultRedirectKeys;
            RedirectDiskDrives = CoreRuntime.Settings.ConDefaultRedirectDiskDrives 
                ? RDPDiskDrives.All 
                : RDPDiskDrives.None;
            RedirectDiskDrivesCustom = CoreRuntime.Settings.ConDefaultRedirectDiskDrivesCustom;
            RedirectPrinters = CoreRuntime.Settings.ConDefaultRedirectPrinters;
            RedirectClipboard = CoreRuntime.Settings.ConDefaultRedirectClipboard;
            RedirectPorts = CoreRuntime.Settings.ConDefaultRedirectPorts;
            RedirectSmartCards = CoreRuntime.Settings.ConDefaultRedirectSmartCards;
            RedirectAudioCapture = CoreRuntime.Settings.ConDefaultRedirectAudioCapture;
            RedirectSound = (RDPSounds)Enum.Parse(typeof(RDPSounds), CoreRuntime.Settings.ConDefaultRedirectSound);
            SoundQuality = (RDPSoundQuality)Enum.Parse(typeof(RDPSoundQuality), CoreRuntime.Settings.ConDefaultSoundQuality);
        }

        private void SetMiscDefaults()
        {
            PreExtApp = CoreRuntime.Settings.ConDefaultPreExtApp;
            PostExtApp = CoreRuntime.Settings.ConDefaultPostExtApp;
            MacAddress = CoreRuntime.Settings.ConDefaultMacAddress;
            UserField = CoreRuntime.Settings.ConDefaultUserField;
            EnvironmentTags = CoreRuntime.Settings.ConDefaultEnvironmentTags;
            Favorite = CoreRuntime.Settings.ConDefaultFavorite;
            RDPStartProgram = CoreRuntime.Settings.ConDefaultRDPStartProgram;
            RDPStartProgramWorkDir = CoreRuntime.Settings.ConDefaultRDPStartProgramWorkDir;
            OpeningCommand = CoreRuntime.Settings.OpeningCommand;
        }

        private void SetVncDefaults()
        {
            VNCCompression = (Compression)Enum.Parse(typeof(Compression), CoreRuntime.Settings.ConDefaultVNCCompression);
            VNCEncoding = (Encoding)Enum.Parse(typeof(Encoding), CoreRuntime.Settings.ConDefaultVNCEncoding);
            VNCAuthMode = (AuthMode)Enum.Parse(typeof(AuthMode), CoreRuntime.Settings.ConDefaultVNCAuthMode);
            VNCProxyType = (ProxyType)Enum.Parse(typeof(ProxyType), CoreRuntime.Settings.ConDefaultVNCProxyType);
            VNCProxyIP = CoreRuntime.Settings.ConDefaultVNCProxyIP;
            VNCProxyPort = CoreRuntime.Settings.ConDefaultVNCProxyPort;
            VNCProxyUsername = CoreRuntime.Settings.ConDefaultVNCProxyUsername;
            VNCProxyPassword = CoreRuntime.Settings.ConDefaultVNCProxyPassword;
            VNCColors = (Colors)Enum.Parse(typeof(Colors), CoreRuntime.Settings.ConDefaultVNCColors);
            VNCSmartSizeMode = (SmartSizeMode)Enum.Parse(typeof(SmartSizeMode), CoreRuntime.Settings.ConDefaultVNCSmartSizeMode);
            VNCViewOnly = CoreRuntime.Settings.ConDefaultVNCViewOnly;
        }

        private void SetNonBrowsablePropertiesDefaults()
        {
            _inheritance = new ConnectionInfoInheritance(this);
            SetNewOpenConnectionList();
        }

        private void SetNewOpenConnectionList()
        {
            OpenConnections = [];
            OpenConnections.CollectionChanged += (sender, args) => RaisePropertyChangedEvent(this, new PropertyChangedEventArgs("OpenConnections"));
        }

        #endregion
    }
}