using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using mRemoteNG.Vnc.Security;
using NUnit.Framework;

namespace mRemoteNG.Vnc.Tests;

public class RfbHandshakeTests
{
    private static readonly byte[] Challenge = Convert.FromHexString("0123456789abcdeffedcba9876543210");

    // Computed independently: openssl enc -des-ecb with the mirrored password bytes as key.
    [TestCase("secret12", "eb0e1edacbc03343f6fc62449e83f214")]
    [TestCase("secret12345", "eb0e1edacbc03343f6fc62449e83f214")]
    [TestCase("pw", "d9eecfd4b6efab137bafbf5ec61d5f04")]
    public void VncAuthenticationEncryptsTheChallengeWithThePassword(string password, string expected)
    {
        Assert.That(Convert.ToHexString(VncAuthentication.Respond(Challenge, password)), Is.EqualTo(expected).IgnoreCase);
    }

    [Test]
    public async Task AServerWithoutAuthenticationIsLoggedIntoAndSetUp()
    {
        using RfbTestServer server = new();
        Task<RfbClient> connecting = RfbClient.ConnectAsync("127.0.0.1", server.Port, new RfbLogin(), CancellationToken.None);

        Assert.That(await server.OfferAsync(1), Is.EqualTo(1));
        server.Accept(800, 600, "office");
        using RfbClient client = await connecting;

        Assert.That((client.Framebuffer.Width, client.Framebuffer.Height, client.DesktopName), Is.EqualTo((800, 600, "office")));
        Assert.That(server.SetPixelFormat, Is.EqualTo(new byte[] { 0, 0, 0, 0, 32, 24, 0, 1, 0, 255, 0, 255, 0, 255, 16, 8, 0, 0, 0, 0 }));
        Assert.That(server.Encodings, Is.EqualTo(new[] { 1, 16, 5, 2, 0, -239, -223, -224 }));
        Assert.That(server.FirstUpdateRequest, Is.EqualTo(new byte[] { 3, 0, 0, 0, 0, 0, 0x03, 0x20, 0x02, 0x58 }));
    }

    [Test]
    public async Task AVncPasswordIsAnsweredWithTheStoredPassword()
    {
        using RfbTestServer server = new();
        Task<RfbClient> connecting = RfbClient.ConnectAsync("127.0.0.1", server.Port, new RfbLogin { Password = "secret12" }, CancellationToken.None);

        Assert.That(await server.OfferAsync(1, 2), Is.EqualTo(2));
        server.Write(Challenge);
        Assert.That(Convert.ToHexString(server.Read(16)), Is.EqualTo("eb0e1edacbc03343f6fc62449e83f214").IgnoreCase);
        server.Accept(640, 480);
        using RfbClient client = await connecting;
    }

    [Test]
    public async Task AMissingPasswordIsAskedFor()
    {
        using RfbTestServer server = new();
        List<bool> asked = [];
        RfbLogin login = new()
        {
            Ask = needsUsername =>
            {
                asked.Add(needsUsername);
                return new RfbCredentials(string.Empty, "pw");
            }
        };
        Task<RfbClient> connecting = RfbClient.ConnectAsync("127.0.0.1", server.Port, login, CancellationToken.None);

        await server.OfferAsync(2);
        server.Write(Challenge);
        Assert.That(Convert.ToHexString(server.Read(16)), Is.EqualTo("d9eecfd4b6efab137bafbf5ec61d5f04").IgnoreCase);
        server.Accept(640, 480);
        using RfbClient client = await connecting;

        Assert.That(asked, Is.EqualTo(new[] { false }));
    }

    [Test]
    public async Task CancellingThePasswordQuestionCancelsTheConnection()
    {
        using RfbTestServer server = new();
        Task<RfbClient> connecting = RfbClient.ConnectAsync("127.0.0.1", server.Port, new RfbLogin(), CancellationToken.None);

        await server.OfferAsync(2);

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await connecting);
    }

    [Test]
    public async Task ARefusedLoginSaysWhy()
    {
        using RfbTestServer server = new();
        Task<RfbClient> connecting = RfbClient.ConnectAsync("127.0.0.1", server.Port, new RfbLogin { Password = "wrong" }, CancellationToken.None);

        await server.OfferAsync(2);
        server.Write(Challenge);
        server.Read(16);
        server.WriteUInt32(1);
        server.WriteString("Authentication failed");

        RfbAuthenticationException error = (await Assert.ThrowsAsync<RfbAuthenticationException>(async () => await connecting))!;
        Assert.That(error.Message, Is.EqualTo("Authentication failed"));
    }

    [Test]
    public async Task AnUnsupportedKindOfLoginIsReported()
    {
        using RfbTestServer server = new();
        Task<RfbClient> connecting = RfbClient.ConnectAsync("127.0.0.1", server.Port, new RfbLogin(), CancellationToken.None);

        await server.AcceptAsync();
        server.Write("RFB 003.008\n"u8);
        server.Read(12);
        server.Write([2, 19, 18]); // VeNCrypt and TLS only.

        RfbException error = (await Assert.ThrowsAsync<RfbException>(async () => await connecting))!;
        Assert.That(error.Message, Does.Contain("19, 18"));
    }

    [Test]
    public async Task AnOldServerChoosesTheLoginItself()
    {
        using RfbTestServer server = new();
        Task<RfbClient> connecting = RfbClient.ConnectAsync("127.0.0.1", server.Port, new RfbLogin { Password = "pw" }, CancellationToken.None);

        await server.AcceptAsync();
        server.Write("RFB 003.003\n"u8);
        Assert.That(Encoding.ASCII.GetString(server.Read(12)), Is.EqualTo("RFB 003.003\n"));
        server.WriteUInt32(2);
        server.Write(Challenge);
        server.Read(16);
        server.Accept(320, 200);
        using RfbClient client = await connecting;

        Assert.That(client.Framebuffer.Width, Is.EqualTo(320));
    }

    [Test]
    public async Task AppleRemoteDesktopLogsInWithTheEncryptedUserAndPassword()
    {
        using RfbTestServer server = new();
        RfbLogin login = new() { Username = "koen", Password = "geheim", PreferUserLogin = true };
        Task<RfbClient> connecting = RfbClient.ConnectAsync("127.0.0.1", server.Port, login, CancellationToken.None);

        // Apple's server announces version 3.889 and offers its own login next to a VNC password.
        await server.AcceptAsync();
        server.Write("RFB 003.889\n"u8);
        Assert.That(Encoding.ASCII.GetString(server.Read(12)), Is.EqualTo("RFB 003.008\n"));
        server.Write([2, 2, 30]);
        Assert.That(server.Read(1)[0], Is.EqualTo(30));

        (string user, string password) = ServeAppleLogin(server);
        server.Accept(1440, 900);
        using RfbClient client = await connecting;

        Assert.That((user, password), Is.EqualTo(("koen", "geheim")));
    }

    // The server side of Apple Remote Desktop authentication, written from the protocol description.
    private static (string User, string Password) ServeAppleLogin(RfbTestServer server)
    {
        BigInteger prime = BigInteger.Parse(
            "0FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD129024E088A67CC74020BBEA63B139B22514A08798E3404DDEF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7EDEE386BFB5A899FA5AE9F24117C4B1FE649286651ECE65381FFFFFFFFFFFFFFFF",
            System.Globalization.NumberStyles.HexNumber);
        const int keyLength = 128;
        BigInteger serverPrivate = new(RandomNumberGenerator.GetBytes(64), isUnsigned: true, isBigEndian: true);
        BigInteger serverPublic = BigInteger.ModPow(2, serverPrivate, prime);

        byte[] parameters = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(parameters, 2);
        BinaryPrimitives.WriteUInt16BigEndian(parameters.AsSpan(2), keyLength);
        server.Write(parameters);
        server.Write(AppleRemoteDesktopAuthentication.ToFixedLength(prime, keyLength));
        server.Write(AppleRemoteDesktopAuthentication.ToFixedLength(serverPublic, keyLength));

        byte[] encrypted = server.Read(128);
        BigInteger clientPublic = new(server.Read(keyLength), isUnsigned: true, isBigEndian: true);
        BigInteger shared = BigInteger.ModPow(clientPublic, serverPrivate, prime);
        using Aes aes = Aes.Create();
        aes.Key = MD5.HashData(AppleRemoteDesktopAuthentication.ToFixedLength(shared, keyLength));
        byte[] login = aes.DecryptEcb(encrypted, PaddingMode.None);
        return (Field(login.AsSpan(0, 64)), Field(login.AsSpan(64, 64)));
    }

    private static string Field(ReadOnlySpan<byte> field) => Encoding.UTF8.GetString(field[..field.IndexOf((byte)0)]);
}
