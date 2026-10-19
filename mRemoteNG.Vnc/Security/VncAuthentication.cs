using System.Text;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace mRemoteNG.Vnc.Security;

/// <summary>
/// VNC authentication (RFB security type 2): the server's 16-byte challenge, encrypted with DES using the password
/// (its first 8 bytes, each byte's bits mirrored, as every VNC implementation does) as key.
/// </summary>
internal static class VncAuthentication
{
    public const byte SecurityType = 2;

    public static byte[] Respond(ReadOnlySpan<byte> challenge, string password)
    {
        if (challenge.Length != 16)
            throw new ArgumentException("A VNC challenge is 16 bytes.", nameof(challenge));

        byte[] key = new byte[8];
        byte[] secret = Encoding.Latin1.GetBytes(password);
        for (int i = 0; i < key.Length && i < secret.Length; i++)
            key[i] = Mirror(secret[i]);

        DesEngine des = new();
        des.Init(forEncryption: true, new KeyParameter(key));
        byte[] response = new byte[16];
        des.ProcessBlock(challenge.ToArray(), 0, response, 0);
        des.ProcessBlock(challenge.ToArray(), 8, response, 8);
        return response;
    }

    private static byte Mirror(byte value)
    {
        byte mirrored = 0;
        for (int bit = 0; bit < 8; bit++)
        {
            if ((value & (1 << bit)) != 0)
                mirrored |= (byte)(0x80 >> bit);
        }

        return mirrored;
    }
}
