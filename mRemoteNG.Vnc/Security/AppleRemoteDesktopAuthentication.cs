using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace mRemoteNG.Vnc.Security;

/// <summary>
/// Apple Remote Desktop authentication (RFB security type 30), which macOS Screen Sharing offers for logging in with a
/// user account: a Diffie-Hellman exchange whose shared secret, hashed with MD5, is the AES-128 key that encrypts the
/// user name and password.
/// </summary>
internal static class AppleRemoteDesktopAuthentication
{
    public const byte SecurityType = 30;

    private const int FieldLength = 64;

    /// <summary>The server's Diffie-Hellman parameters, as it sends them.</summary>
    public sealed record Parameters(ushort Generator, byte[] Prime, byte[] ServerPublicKey);

    /// <summary>What the client sends back: the encrypted login, then its public key (as long as the prime).</summary>
    public static byte[] Respond(Parameters server, string username, string password)
    {
        int keyLength = server.Prime.Length;
        BigInteger prime = new(server.Prime, isUnsigned: true, isBigEndian: true);
        BigInteger serverPublic = new(server.ServerPublicKey, isUnsigned: true, isBigEndian: true);
        if (prime < 3 || serverPublic <= 1 || serverPublic >= prime - 1)
            throw new RfbException("The server sent invalid Apple Remote Desktop parameters.");

        BigInteger random = new(RandomNumberGenerator.GetBytes(keyLength), isUnsigned: true, isBigEndian: true);
        BigInteger privateKey = random % (prime - 2) + 1;
        BigInteger clientPublic = BigInteger.ModPow(server.Generator, privateKey, prime);
        BigInteger shared = BigInteger.ModPow(serverPublic, privateKey, prime);

        byte[] key = MD5.HashData(ToFixedLength(shared, keyLength));
        byte[] login = RandomNumberGenerator.GetBytes(2 * FieldLength);
        WriteField(login.AsSpan(0, FieldLength), username);
        WriteField(login.AsSpan(FieldLength, FieldLength), password);

        using Aes aes = Aes.Create();
        aes.Key = key;
        byte[] encrypted = aes.EncryptEcb(login, PaddingMode.None);
        return [.. encrypted, .. ToFixedLength(clientPublic, keyLength)];
    }

    // A NUL-terminated string in a 64-byte field; the rest of the field keeps its random bytes.
    private static void WriteField(Span<byte> field, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length >= FieldLength)
            throw new RfbException("The user name or password is too long for Apple Remote Desktop (63 bytes at most).");
        bytes.CopyTo(field);
        field[bytes.Length] = 0;
    }

    internal static byte[] ToFixedLength(BigInteger value, int length)
    {
        byte[] bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length >= length)
            return bytes[^length..];
        byte[] padded = new byte[length];
        bytes.CopyTo(padded, length - bytes.Length);
        return padded;
    }
}
