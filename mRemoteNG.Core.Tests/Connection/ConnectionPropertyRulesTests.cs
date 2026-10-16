using System.ComponentModel;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Connection.Protocol.RDP;
using mRemoteNG.Container;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNG.Core.Tests.Connection
{
    public class ConnectionPropertyRulesTests
    {
        [Test]
        public void RootNodeShowsNameAndPassword()
        {
            Assert.That(ConnectionPropertyRules.RootNodeProperties(new RootNodeInfo(RootNodeType.Connection)),
                        Is.EqualTo(new[] { "Name", "Password" }));
        }

        [Test]
        public void PuttySessionsRootShowsOnlyName()
        {
            Assert.That(ConnectionPropertyRules.RootNodeProperties(new RootPuttySessionsNodeInfo()),
                        Is.EqualTo(new[] { "Name" }));
        }

        [TestCase(nameof(ConnectionInfo.Hostname), ProtocolType.SSH2, true)]
        [TestCase(nameof(ConnectionInfo.RDGatewayHostname), ProtocolType.RDP, true)]
        [TestCase(nameof(ConnectionInfo.RDGatewayHostname), ProtocolType.SSH2, false)]
        [TestCase(nameof(ConnectionInfo.PuttySession), ProtocolType.SSH2, true)]
        [TestCase(nameof(ConnectionInfo.PuttySession), ProtocolType.RDP, false)]
        public void PropertyAppliesOnlyToItsProtocols(string propertyName, ProtocolType protocol, bool expected)
        {
            PropertyDescriptor property = TypeDescriptor.GetProperties(typeof(ConnectionInfo))[propertyName];

            Assert.That(ConnectionPropertyRules.IsValidForProtocol(property, protocol, skipProtocolCheck: false), Is.EqualTo(expected));
        }

        [Test]
        public void SkippingTheProtocolCheckKeepsEveryBrowsableProperty()
        {
            PropertyDescriptor property = TypeDescriptor.GetProperties(typeof(ConnectionInfo))[nameof(ConnectionInfo.RDGatewayHostname)];

            Assert.That(ConnectionPropertyRules.IsValidForProtocol(property, ProtocolType.SSH2, skipProtocolCheck: true), Is.True);
        }

        [Test]
        public void InheritedPropertiesAreHidden()
        {
            ConnectionInfo connection = new() { Protocol = ProtocolType.SSH2 };
            connection.Inheritance.Username = true;

            Assert.That(ConnectionPropertyRules.HiddenConnectionProperties(connection), Does.Contain(nameof(ConnectionInfo.Username)));
        }

        [Test]
        public void GatewaySettingsAreHiddenWhenTheGatewayIsNeverUsed()
        {
            ConnectionInfo connection = new() { Protocol = ProtocolType.RDP, RDGatewayUsageMethod = RDGatewayUsageMethod.Never };

            Assert.That(ConnectionPropertyRules.HiddenConnectionProperties(connection),
                        Does.Contain(nameof(ConnectionInfo.RDGatewayHostname)).And.Contain(nameof(ConnectionInfo.RDGatewayPassword)));
        }

        [Test]
        public void FoldersHaveNoHostname()
        {
            Assert.That(ConnectionPropertyRules.HiddenConnectionProperties(new ContainerInfo()), Does.Contain(nameof(ConnectionInfo.Hostname)));
        }

        [Test]
        public void VaultSettingsAreHiddenWithoutExternalCredentialProvider()
        {
            ConnectionInfo connection = new() { Protocol = ProtocolType.SSH2, ExternalCredentialProvider = ExternalCredentialProvider.None };

            Assert.That(ConnectionPropertyRules.HiddenConnectionProperties(connection), Does.Contain(nameof(ConnectionInfo.VaultOpenbaoMount)));
        }
    }
}
