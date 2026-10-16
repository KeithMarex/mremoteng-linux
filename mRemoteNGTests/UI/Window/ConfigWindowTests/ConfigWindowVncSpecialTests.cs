using System.Threading;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Connection.Protocol.VNC;
using NUnit.Framework;

namespace mRemoteNGTests.UI.Window.ConfigWindowTests
{
    [Apartment(ApartmentState.STA)]
    public class ConfigWindowVncSpecialTests : ConfigWindowSpecialTestsBase
    {
        protected override ProtocolType Protocol => ProtocolType.VNC;

        [Test]
        public void UserDomainPropertiesShown_WhenAuthModeIsWindows()
        {
            ConnectionInfo.VNCAuthMode = AuthMode.AuthWin;
            ExpectedPropertyList.AddRange(new []
            {
                nameof(ConnectionInfo.Username),
                nameof(ConnectionInfo.Domain),
            });
        }

        [TestCase(ProxyType.ProxyHTTP)]
        [TestCase(ProxyType.ProxySocks5)]
        [TestCase(ProxyType.ProxyUltra)]
        public void ProxyPropertiesShown_WhenProxyModeIsNotNone(ProxyType proxyType)
        {
            ConnectionInfo.VNCProxyType = proxyType;
            ExpectedPropertyList.AddRange(new[]
            {
                nameof(ConnectionInfo.VNCProxyIP),
                nameof(ConnectionInfo.VNCProxyPort),
                nameof(ConnectionInfo.VNCProxyUsername),
                nameof(ConnectionInfo.VNCProxyPassword),
            });
        }
    }
}
