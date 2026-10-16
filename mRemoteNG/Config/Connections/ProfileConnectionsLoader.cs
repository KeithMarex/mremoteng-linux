using System.Runtime.Versioning;
using mRemoteNG.Config.Serializers;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Config.UserProfiles;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using mRemoteNG.UI;

namespace mRemoteNG.Config.Connections
{
    [SupportedOSPlatform("windows")]
    internal sealed class ProfileConnectionsLoader(IConnectionProfileDataProvider dataProvider) : IConnectionsLoader
    {
        public ConnectionTreeModel Load()
        {
            string serializedConnections = dataProvider.Load();
            if (string.IsNullOrEmpty(serializedConnections))
            {
                ConnectionTreeModel model = new();
                model.AddRootNode(new RootNodeInfo(RootNodeType.Connection));
                return model;
            }
            try
            {
                return new XmlConnectionsDeserializer().Deserialize(serializedConnections);
            }
            catch (UnsupportedConnectionFileVersionException ex)
            {
                DialogFactory.ShowIncompatibleConnectionFileDialog(string.Empty, ex);
                throw;
            }
        }
    }
}
