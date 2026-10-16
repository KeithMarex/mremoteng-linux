using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Connection.Protocol.RDP;
using mRemoteNG.Connection.Protocol.VNC;
using mRemoteNG.Tools.Attributes;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Connection
{
    /// <summary>
    /// Which connection properties a property editor shows, shared by the WinForms property grid and the
    /// Linux app: properties must apply to the connection's protocol, and some only make sense for certain
    /// values of other properties.
    /// </summary>
    public static class ConnectionPropertyRules
    {
        /// <summary>The properties shown for a root node.</summary>
        public static string[] RootNodeProperties(RootNodeInfo rootNode)
        {
            return rootNode is RootPuttySessionsNodeInfo
                ? [nameof(RootPuttySessionsNodeInfo.Name)]
                : [nameof(RootNodeInfo.Name), nameof(RootNodeInfo.Password)];
        }

        /// <summary>The properties hidden when editing the default connection.</summary>
        public static string[] DefaultConnectionHiddenProperties { get; } =
            [nameof(AbstractConnectionRecord.Hostname), nameof(AbstractConnectionRecord.Name)];

        public static bool IsValidForProtocol(PropertyDescriptor property, ProtocolType protocol, bool skipProtocolCheck)
        {
            return
                property.Attributes[typeof(BrowsableAttribute)] is not BrowsableAttribute { Browsable: false } &&
                (skipProtocolCheck || (property.Attributes[typeof(AttributeUsedInProtocol)] as AttributeUsedInProtocol)?
                    .SupportedProtocolTypes
                    .Contains(protocol) != false);
        }

        /// <summary>
        /// Properties of <paramref name="connection"/> to hide while editing it: the inherited ones, and the
        /// ones its current settings make irrelevant.
        /// </summary>
        public static List<string> HiddenConnectionProperties(ConnectionInfo connection)
        {
            List<string> strHide = new();

            // hide any inherited properties
            strHide.AddRange(connection.Inheritance.GetEnabledInheritanceProperties());

            // hide external provider fields
            strHide.AddRange(ExternalAddressProviderExclusions());
            strHide.AddRange(ExternalCredentialProviderExclusions(connection));

            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (connection.Protocol)
            {
                case ProtocolType.RDP:
                    strHide.AddRange(RdpExclusions(connection));
                    break;
                case ProtocolType.VNC:
                case ProtocolType.ARD:
                    strHide.AddRange(VncExclusions(connection));
                    break;
            }

            if (connection.IsContainer)
                strHide.Add(nameof(AbstractConnectionRecord.Hostname));

            return strHide;
        }

        private static List<string> ExternalAddressProviderExclusions()
        {
            return
            [
                nameof(AbstractConnectionRecord.ExternalAddressProvider),
                nameof(AbstractConnectionRecord.EC2InstanceId),
                nameof(AbstractConnectionRecord.EC2Region)
            ];
        }

        private static List<string> ExternalCredentialProviderExclusions(ConnectionInfo connection)
        {
            List<string> strHide = new();

            if (connection.ExternalCredentialProvider == ExternalCredentialProvider.None)
            {
                strHide.Add(nameof(AbstractConnectionRecord.UserViaAPI));
                strHide.Add(nameof(AbstractConnectionRecord.VaultOpenbaoSecretEngine));
                strHide.Add(nameof(AbstractConnectionRecord.VaultOpenbaoMount));
                strHide.Add(nameof(AbstractConnectionRecord.VaultOpenbaoRole));
            }
            else if (connection.ExternalCredentialProvider == ExternalCredentialProvider.DelineaSecretServer
                     || connection.ExternalCredentialProvider == ExternalCredentialProvider.ClickstudiosPasswordState)
            {
                strHide.Add(nameof(AbstractConnectionRecord.Username));
                strHide.Add(nameof(AbstractConnectionRecord.Password));
                strHide.Add(nameof(AbstractConnectionRecord.Domain));
                strHide.Add(nameof(AbstractConnectionRecord.VaultOpenbaoSecretEngine));
                strHide.Add(nameof(AbstractConnectionRecord.VaultOpenbaoMount));
                strHide.Add(nameof(AbstractConnectionRecord.VaultOpenbaoRole));
            }
            else if (connection.ExternalCredentialProvider == ExternalCredentialProvider.OnePassword)
            {
                strHide.Add(nameof(AbstractConnectionRecord.VaultOpenbaoSecretEngine));
                strHide.Add(nameof(AbstractConnectionRecord.VaultOpenbaoMount));
                strHide.Add(nameof(AbstractConnectionRecord.VaultOpenbaoRole));
            }
            else if (connection.ExternalCredentialProvider == ExternalCredentialProvider.VaultOpenbao)
            {
                strHide.Add(nameof(AbstractConnectionRecord.UserViaAPI));
                if (connection.VaultOpenbaoSecretEngine != VaultOpenbaoSecretEngine.Kv
                    && connection.VaultOpenbaoSecretEngine != VaultOpenbaoSecretEngine.SSHOTP)
                    strHide.Add(nameof(AbstractConnectionRecord.Username));
                strHide.Add(nameof(AbstractConnectionRecord.Password));
            }

            return strHide;
        }

        private static List<string> RdpExclusions(ConnectionInfo connection)
        {
            List<string> strHide = new();

            if (connection.RDPMinutesToIdleTimeout <= 0)
            {
                strHide.Add(nameof(AbstractConnectionRecord.RDPAlertIdleTimeout));
            }

            if (connection.RDGatewayUsageMethod == RDGatewayUsageMethod.Never)
            {
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayDomain));
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayHostname));
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayPassword));
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayUseConnectionCredentials));
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayUsername));
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayAccessToken));
            }
            else if (connection.RDGatewayUseConnectionCredentials == RDGatewayUseConnectionCredentials.Yes ||
                     connection.RDGatewayUseConnectionCredentials == RDGatewayUseConnectionCredentials.SmartCard)
            {
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayDomain));
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayPassword));
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayUsername));
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayExternalCredentialProvider));
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayUserViaAPI));
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayAccessToken));
            }
            else if (connection.RDGatewayUseConnectionCredentials == RDGatewayUseConnectionCredentials.ExternalCredentialProvider)
            {
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayDomain));
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayPassword));
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayUsername));
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayAccessToken));
            }
            else if (connection.RDGatewayUseConnectionCredentials == RDGatewayUseConnectionCredentials.AccessToken)
            {
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayDomain));
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayPassword));
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayUsername));
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayExternalCredentialProvider));
                strHide.Add(nameof(AbstractConnectionRecord.RDGatewayUserViaAPI));
            }

            if (connection.Resolution != RDPResolutions.Fullscreen)
            {
                strHide.Add(nameof(AbstractConnectionRecord.AutomaticResize));
            }

            if (connection.RedirectDiskDrives != RDPDiskDrives.Custom)
            {
                strHide.Add(nameof(AbstractConnectionRecord.RedirectDiskDrivesCustom));
            }

            if (connection.RedirectSound != RDPSounds.BringToThisComputer)
            {
                strHide.Add(nameof(AbstractConnectionRecord.SoundQuality));
            }

            if (!connection.UseVmId)
            {
                strHide.Add(nameof(AbstractConnectionRecord.VmId));
                strHide.Add(nameof(AbstractConnectionRecord.UseEnhancedMode));
            }

            return strHide;
        }

        private static List<string> VncExclusions(ConnectionInfo connection)
        {
            List<string> strHide = new();
            if (connection.VNCAuthMode == AuthMode.AuthVNC)
            {
                strHide.Add(nameof(AbstractConnectionRecord.Username));
                strHide.Add(nameof(AbstractConnectionRecord.Domain));
            }

            if (connection.VNCProxyType == ProxyType.ProxyNone)
            {
                strHide.Add(nameof(AbstractConnectionRecord.VNCProxyIP));
                strHide.Add(nameof(AbstractConnectionRecord.VNCProxyPassword));
                strHide.Add(nameof(AbstractConnectionRecord.VNCProxyPort));
                strHide.Add(nameof(AbstractConnectionRecord.VNCProxyUsername));
            }

            return strHide;
        }
    }
}
