using mRemoteNG.Tools;
using mRemoteNG.Resources.Language;

namespace mRemoteNG.Connection.Protocol.Http
{
    public enum RenderingEngine
    {
        [LocalizedAttributes.LocalizedDescription(nameof(Language.HttpInternetExplorer))]
        IE = 1,

        [LocalizedAttributes.LocalizedDescription(nameof(Language.HttpCEF))]
        EdgeChromium = 2
    }
}
