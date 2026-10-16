using System;
using System.Runtime.Versioning;
using mRemoteNG.Connection.Protocol.VNC;

namespace mRemoteNG.Connection.Protocol.ARD
{
    [SupportedOSPlatform("windows")]
    public class ProtocolARD : ProtocolVNC
    {
        public ProtocolARD()
        {
        }

        public enum Defaults
        {
            Port = DefaultPorts.Ard
        }
    }
}
