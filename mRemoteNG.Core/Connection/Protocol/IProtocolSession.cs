namespace mRemoteNG.Connection.Protocol
{
    /// <summary>
    /// An open session of a connection, as tracked in <see cref="ProtocolList"/>. Each app implements it
    /// with its own protocol hosts (the WinForms app: ProtocolBase).
    /// </summary>
    public interface IProtocolSession
    {
        void Disconnect();
    }
}
