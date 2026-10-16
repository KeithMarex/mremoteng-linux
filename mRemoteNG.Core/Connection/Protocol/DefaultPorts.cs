namespace mRemoteNG.Connection.Protocol
{
    /// <summary>The port a connection uses by default for each protocol; 0 means the protocol has no port.</summary>
    public static class DefaultPorts
    {
        public const int Rdp = 3389;
        public const int Vnc = 5900;
        public const int Ard = 5900;
        public const int Ssh = 22;
        public const int Telnet = 23;
        public const int Ftp = 21;
        public const int Rlogin = 513;
        public const int Raw = 23;
        public const int Http = 80;
        public const int Https = 443;
        public const int PowerShell = 5985;

        public static int For(ProtocolType protocol)
        {
            return protocol switch
            {
                ProtocolType.RDP => Rdp,
                ProtocolType.VNC => Vnc,
                ProtocolType.ARD => Ard,
                ProtocolType.SSH1 or ProtocolType.SSH2 or ProtocolType.Terminal or ProtocolType.SFTP => Ssh,
                ProtocolType.FTP => Ftp,
                ProtocolType.Telnet => Telnet,
                ProtocolType.Rlogin => Rlogin,
                ProtocolType.RAW => Raw,
                ProtocolType.HTTP => Http,
                ProtocolType.HTTPS => Https,
                ProtocolType.PowerShell => PowerShell,
                _ => 0 // WSL, IntApp and AnyDesk do not connect to a port
            };
        }
    }
}
