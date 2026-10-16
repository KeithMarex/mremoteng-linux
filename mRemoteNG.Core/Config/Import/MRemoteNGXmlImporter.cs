using System;
using System.IO;
using System.Linq;
using System.Security;
using mRemoteNG.App;
using mRemoteNG.Config.DataProviders;
using mRemoteNG.Config.Serializers.ConnectionSerializers.Xml;
using mRemoteNG.Container;
using mRemoteNG.Messages;
using mRemoteNG.Tools;


namespace mRemoteNG.Config.Import
{
    // ReSharper disable once InconsistentNaming
    /// <param name="authenticationRequestor">
    /// Asks for the password of a protected file. Without one, a protected file fails to import.
    /// </param>
    public class MRemoteNGXmlImporter(Func<Optional<SecureString>>? authenticationRequestor = null) : IConnectionImporter<string>
    {
        public void Import(string fileName, ContainerInfo destinationContainer)
        {
            if (fileName == null)
            {
                CoreRuntime.MessageCollector.AddMessage(MessageClass.ErrorMsg, "Unable to import file. File path is null.");
                return;
            }

            if (!File.Exists(fileName))
                CoreRuntime.MessageCollector.AddMessage(MessageClass.ErrorMsg,
                                                    $"Unable to import file. File does not exist. Path: {fileName}");

            FileDataProvider dataProvider = new(fileName);
            string xmlString = dataProvider.Load();
            XmlConnectionsDeserializer xmlConnectionsDeserializer = new(authenticationRequestor ?? (() => Optional<SecureString>.Empty));
            Tree.ConnectionTreeModel connectionTreeModel = xmlConnectionsDeserializer.Deserialize(xmlString, true)
                ?? throw new InvalidDataException($"{fileName} could not be imported: it is empty or the password was not given.");

            ContainerInfo rootImportContainer = new() { Name = Path.GetFileNameWithoutExtension(fileName)};
            rootImportContainer.AddChildRange(connectionTreeModel.RootNodes.First().Children.ToArray());
            destinationContainer.AddChild(rootImportContainer);
        }
    }
}