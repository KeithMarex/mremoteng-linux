using mRemoteNG.Avalonia.Services;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Security;
using mRemoteNG.Vnc;

namespace mRemoteNG.Avalonia.Sessions;

/// <summary>
/// Opens a VNC or Apple Remote Desktop connection with the app's own viewer (mRemoteNG.Vnc), as the Windows app does
/// with VncSharp. A missing password (or, for ARD, user name) is asked for when the server wants it.
/// </summary>
public sealed class VncConnector(IDialogService dialogs)
{
    /// <exception cref="SessionException">The connection failed or was refused, with the reason.</exception>
    /// <exception cref="OperationCanceledException">The login was cancelled.</exception>
    public async Task<RfbClient> ConnectAsync(ConnectionInfo connection, CancellationToken cancellation)
    {
        string host = SessionPlanner.Host(connection);
        int port = SessionPlanner.Port(connection);
        bool apple = connection.Protocol == ProtocolType.ARD;
        RfbLogin login = new()
        {
            Username = apple ? connection.Username.Trim() : string.Empty,
            // A new connection's password is null until one is set.
            Password = connection.Password ?? string.Empty,
            PreferUserLogin = apple,
            // Called on the connection's worker thread, which waits for the answer. Through an SSH tunnel, the server's name.
            Ask = needsUsername => Ask(TunnelLease.ServerOf(connection)?.Host ?? host, needsUsername, connection)
        };

        try
        {
            return await RfbClient.ConnectAsync(host, port, login, cancellation);
        }
        catch (Exception ex) when (ex is RfbException or RfbAuthenticationException or System.Net.Sockets.SocketException)
        {
            throw new SessionException(ex.Message);
        }
    }

    private RfbCredentials? Ask(string host, bool needsUsername, ConnectionInfo connection)
    {
        if (needsUsername)
        {
            LoginCredentials initial = new(connection.Username.Trim(), string.Empty, string.Empty);
            LoginCredentials? login = dialogs.AskLoginAsync(UiText.LoginPrompt(host), initial).GetAwaiter().GetResult();
            return login is null ? null : new RfbCredentials(login.Username, login.Password);
        }

        string? password = dialogs.AskSecretAsync(UiText.VncPasswordPrompt(host)).GetAwaiter().GetResult()?.ConvertToUnsecureString();
        return password is null ? null : new RfbCredentials(string.Empty, password);
    }
}
