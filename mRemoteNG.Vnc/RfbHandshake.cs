using System.Text;
using mRemoteNG.Vnc.Security;

namespace mRemoteNG.Vnc;

/// <summary>A login for a VNC server: what the connection stores, and how to ask for what is missing.</summary>
public sealed class RfbLogin
{
    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    /// <summary>Log in with a user account (Apple Remote Desktop) when the server offers it, rather than a VNC password.</summary>
    public bool PreferUserLogin { get; init; }

    /// <summary>Asks for a missing login; the argument says whether a user name is needed. Null when cancelled.</summary>
    public Func<bool, RfbCredentials?> Ask { get; init; } = _ => null;
}

/// <summary>A user name (empty for a VNC password) and password, as typed.</summary>
public sealed record RfbCredentials(string Username, string Password);

/// <summary>
/// The start of an RFB session (RFC 6143 §7.1–7.3): agreeing on the protocol version, logging in, and reading the
/// server's screen size and name.
/// </summary>
internal static class RfbHandshake
{
    private const byte NoAuthentication = 1;

    public sealed record ServerInit(int Width, int Height, string Name);

    /// <exception cref="RfbException">The server refused, or speaks something this client does not.</exception>
    public static ServerInit Run(RfbStream input, Stream output, RfbLogin login)
    {
        int minor = AgreeVersion(input, output);
        LogIn(input, output, login, minor);

        // ClientInit: share the desktop with other viewers, as VncSharp does on Windows.
        output.Write([1]);
        int width = input.ReadUInt16();
        int height = input.ReadUInt16();
        input.Skip(16); // The server's own pixel format; the client asks for its own.
        string name = input.ReadString(64 * 1024, Encoding.UTF8);
        return new ServerInit(width, height, name);
    }

    // "RFB 003.008\n"; Apple answers 003.889, older servers 003.003 to 003.007.
    private static int AgreeVersion(RfbStream input, Stream output)
    {
        string version = Encoding.ASCII.GetString(input.ReadBytes(12));
        if (!version.StartsWith("RFB 003.", StringComparison.Ordinal) || !int.TryParse(version.AsSpan(8, 3), out int minor))
            throw new RfbException("The server did not answer as a VNC server.");

        int agreed = minor >= 8 ? 8 : minor == 7 ? 7 : 3;
        output.Write(Encoding.ASCII.GetBytes($"RFB 003.00{agreed}\n"));
        return agreed;
    }

    private static void LogIn(RfbStream input, Stream output, RfbLogin login, int minor)
    {
        byte type;
        if (minor == 3)
        {
            // Version 3.3: the server decides.
            type = (byte)input.ReadUInt32();
            if (type == 0)
                throw new RfbException(input.ReadString(64 * 1024, Encoding.UTF8));
        }
        else
        {
            int count = input.ReadByte();
            if (count == 0)
                throw new RfbException(input.ReadString(64 * 1024, Encoding.UTF8));
            type = Choose(input.ReadBytes(count), login);
            output.Write([type]);
        }

        switch (type)
        {
            case NoAuthentication:
                // Before 3.8 a session without authentication gets no result.
                if (minor < 8)
                    return;
                break;
            case VncAuthentication.SecurityType:
                string password = login.Password.Length > 0
                    ? login.Password
                    : login.Ask(false)?.Password ?? throw new OperationCanceledException();
                output.Write(VncAuthentication.Respond(input.ReadBytes(16), password));
                break;
            case AppleRemoteDesktopAuthentication.SecurityType:
                RfbCredentials credentials = login.Username.Length > 0 && login.Password.Length > 0
                    ? new RfbCredentials(login.Username, login.Password)
                    : login.Ask(true) ?? throw new OperationCanceledException();
                ushort generator = input.ReadUInt16();
                int keyLength = input.ReadUInt16();
                if (keyLength is < 16 or > 1024)
                    throw new RfbException("The server sent invalid Apple Remote Desktop parameters.");
                AppleRemoteDesktopAuthentication.Parameters parameters = new(generator, input.ReadBytes(keyLength), input.ReadBytes(keyLength));
                output.Write(AppleRemoteDesktopAuthentication.Respond(parameters, credentials.Username, credentials.Password));
                break;
            default:
                throw new RfbException($"The server asks for a kind of login (type {type}) this app does not support.");
        }

        if (input.ReadUInt32() != 0)
        {
            string reason = minor >= 8 ? input.ReadString(64 * 1024, Encoding.UTF8) : string.Empty;
            throw new RfbAuthenticationException(reason.Length > 0 ? reason : "The server refused the login.");
        }
    }

    // A user account for Apple Remote Desktop connections (or when a user name is stored), else a VNC password, else none.
    private static byte Choose(byte[] offered, RfbLogin login)
    {
        bool userLogin = login.PreferUserLogin || login.Username.Length > 0;
        byte[] preference = userLogin
            ? [AppleRemoteDesktopAuthentication.SecurityType, VncAuthentication.SecurityType, NoAuthentication]
            : [VncAuthentication.SecurityType, NoAuthentication, AppleRemoteDesktopAuthentication.SecurityType];
        foreach (byte type in preference)
        {
            if (offered.Contains(type))
                return type;
        }

        throw new RfbException($"The server only offers kinds of login this app does not support ({string.Join(", ", offered)}).");
    }
}

/// <summary>The server refused the login (a wrong password, for example).</summary>
public sealed class RfbAuthenticationException(string message) : Exception(message);
