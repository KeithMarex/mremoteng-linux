using mRemoteNG.Connection.Protocol;
using NUnit.Framework;

namespace mRemoteNG.Core.Tests.Connection.Protocol
{
    public class DefaultPortsTests
    {
        // Same mapping ConnectionInfo.GetDefaultPort used before it moved to Core.
        [TestCase(ProtocolType.RDP, 3389)]
        [TestCase(ProtocolType.VNC, 5900)]
        [TestCase(ProtocolType.ARD, 5900)]
        [TestCase(ProtocolType.SSH1, 22)]
        [TestCase(ProtocolType.SSH2, 22)]
        [TestCase(ProtocolType.Telnet, 23)]
        [TestCase(ProtocolType.Rlogin, 513)]
        [TestCase(ProtocolType.RAW, 23)]
        [TestCase(ProtocolType.HTTP, 80)]
        [TestCase(ProtocolType.HTTPS, 443)]
        [TestCase(ProtocolType.PowerShell, 5985)]
        [TestCase(ProtocolType.Terminal, 22)]
        [TestCase(ProtocolType.WSL, 0)]
        [TestCase(ProtocolType.AnyDesk, 0)]
        [TestCase(ProtocolType.IntApp, 0)]
        public void ReturnsDefaultPortForProtocol(ProtocolType protocol, int expectedPort)
        {
            Assert.That(DefaultPorts.For(protocol), Is.EqualTo(expectedPort));
        }
    }
}
