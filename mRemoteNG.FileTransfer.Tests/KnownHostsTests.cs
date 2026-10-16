using System.Runtime.Versioning;
using mRemoteNG.FileTransfer.Sftp;
using NUnit.Framework;

namespace mRemoteNG.FileTransfer.Tests;

public class KnownHostsTests
{
    // Throwaway public keys, with the fingerprints ssh-keygen -l shows for them.
    private const string Ed25519A = "AAAAC3NzaC1lZDI1NTE5AAAAIDtpq7TnK7EDEgz0Dp05n6y7jQ8sSDPUUVpgiLBy9zB9";
    private const string Ed25519AFingerprint = "SHA256:Rj/vLCodAsdAjIBZbIGjIvkYErgPoCrTkZ8f0rvEgGc";
    private const string Ed25519B = "AAAAC3NzaC1lZDI1NTE5AAAAIIjIxhy/QxuOOigJjp/PVUhinBOpLjGu7LUfd0rQBF4C";
    private const string Ecdsa = "AAAAE2VjZHNhLXNoYTItbmlzdHAyNTYAAAAIbmlzdHAyNTYAAABBBNU5uF5NpjtphwcbWsAC4FCcvqslzdwz149s400dgIa+lLQCaQ1wDbiXTqrG+NL8uvvVneDxI8+GVZPpCoveQ00=";

    private string _directory = null!;

    [SetUp]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "mremoteng-filetransfer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void Teardown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public void AKnownKeyIsTrustedAndAnotherOfTheSameTypeIsAChange()
    {
        KnownHosts knownHosts = Load($"web1.example ssh-ed25519 {Ed25519A}");

        Assert.That(knownHosts.Check("web1.example", 22, Key(Ed25519A)).Status, Is.EqualTo(HostKeyStatus.Trusted));
        HostKeyCheck changed = knownHosts.Check("WEB1.example", 22, Key(Ed25519B));
        Assert.That(changed.Status, Is.EqualTo(HostKeyStatus.Changed));
        Assert.That(changed.Conflict, Does.EndWith("known_hosts:1"));
    }

    [Test]
    public void AKeyOfAnotherTypeIsUnknownButNamesTheKnownTypes()
    {
        HostKeyCheck check = Load($"web1.example ssh-ed25519 {Ed25519A}").Check("web1.example", 22, Key(Ecdsa));

        Assert.That((check.Status, check.KnownKeyTypes), Is.EqualTo((HostKeyStatus.Unknown, (IReadOnlyList<string>)["ssh-ed25519"])));
    }

    [Test]
    public void OtherPortsAreWrittenInBrackets()
    {
        KnownHosts knownHosts = Load($"[web1.example]:2222 ssh-ed25519 {Ed25519A}");

        Assert.That(knownHosts.Check("web1.example", 2222, Key(Ed25519A)).Status, Is.EqualTo(HostKeyStatus.Trusted));
        Assert.That(knownHosts.Check("web1.example", 22, Key(Ed25519A)).Status, Is.EqualTo(HostKeyStatus.Unknown));
    }

    [Test]
    public void LinesHashedByOpenSshMatch()
    {
        // ssh-keygen -H of "[files.example]:2222 ..." and "web1.example ...".
        KnownHosts knownHosts = Load(
            $"|1|qg7sdSMDUlTLMd5Fluq6T/DPn+c=|eiwDXlByj00uuOnutOW5jXecrJM= ssh-ed25519 {Ed25519A}",
            $"|1|/R1aG7lpGNLiOuJdQf9xP+ZRVvQ=|clMynrY+qoolkSHe/J3kLbDozVg= ecdsa-sha2-nistp256 {Ecdsa}");

        Assert.That(knownHosts.Check("files.example", 2222, Key(Ed25519A)).Status, Is.EqualTo(HostKeyStatus.Trusted));
        Assert.That(knownHosts.Check("web1.example", 22, Key(Ecdsa)).Status, Is.EqualTo(HostKeyStatus.Trusted));
        Assert.That(knownHosts.Check("files.example", 22, Key(Ed25519A)).Status, Is.EqualTo(HostKeyStatus.Unknown));
    }

    [TestCase("*.example.com,!build.example.com", "web.example.com", true)]
    [TestCase("*.example.com,!build.example.com", "build.example.com", false)]
    [TestCase("web?.example.com", "web1.example.com", true)]
    [TestCase("web?.example.com", "web12.example.com", false)]
    [TestCase("10.0.0.5,db.example.com", "db.example.com", true)]
    public void HostPatternsMatchLikeOpenSsh(string patterns, string host, bool matches)
    {
        Assert.That(KnownHosts.Matches(patterns, host), Is.EqualTo(matches));
    }

    [Test]
    public void ARevokedKeyIsNeverTrusted()
    {
        KnownHosts knownHosts = Load($"@revoked * ssh-ed25519 {Ed25519A}", $"web1.example ssh-ed25519 {Ed25519A}");

        Assert.That(knownHosts.Check("web1.example", 22, Key(Ed25519A)).Status, Is.EqualTo(HostKeyStatus.Revoked));
    }

    [Test]
    public void CertificateAuthoritiesAndBrokenLinesAreSkipped()
    {
        KnownHosts knownHosts = Load($"@cert-authority *.example ssh-ed25519 {Ed25519B}", "garbage", "# comment",
                                     $"web1.example ssh-ed25519 not-base64!", $"web1.example ssh-ed25519 {Ed25519A}");

        Assert.That(knownHosts.Check("web1.example", 22, Key(Ed25519A)).Status, Is.EqualTo(HostKeyStatus.Trusted));
        Assert.That(knownHosts.KeyTypesFor("other.example", 22), Is.Empty);
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public void AnAddedKeyIsHashedIntoAPrivateFileAndThenTrusted()
    {
        string file = Path.Combine(_directory, ".ssh", "known_hosts");

        KnownHosts.Add(file, "files.example", 2222, Key(Ed25519A), hash: true);

        string line = File.ReadAllText(file);
        Assert.That(line, Does.StartWith("|1|").And.EndWith($" ssh-ed25519 {Ed25519A}\n").And.Not.Contain("files.example"));
        Assert.That(File.GetUnixFileMode(file), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        Assert.That(KnownHosts.Load([file]).Check("files.example", 2222, Key(Ed25519A)).Status, Is.EqualTo(HostKeyStatus.Trusted));
    }

    [Test]
    public void AKeyIsAppendedOnItsOwnLine()
    {
        string file = Path.Combine(_directory, "known_hosts");
        File.WriteAllText(file, $"web1.example ssh-ed25519 {Ed25519B}");

        KnownHosts.Add(file, "web2.example", 22, Key(Ed25519A), hash: false);

        Assert.That(File.ReadAllLines(file), Is.EqualTo(new[] { $"web1.example ssh-ed25519 {Ed25519B}", $"web2.example ssh-ed25519 {Ed25519A}" }));
    }

    [Test]
    public void TheFingerprintIsTheOneSshShows()
    {
        Assert.That(KnownHosts.Fingerprint(Key(Ed25519A)), Is.EqualTo(Ed25519AFingerprint));
        Assert.That(KnownHosts.KeyType(Key(Ecdsa)), Is.EqualTo("ecdsa-sha2-nistp256"));
    }

    private KnownHosts Load(params string[] lines)
    {
        string file = Path.Combine(_directory, "known_hosts");
        File.WriteAllLines(file, lines);
        return KnownHosts.Load([file, Path.Combine(_directory, "missing")]);
    }

    private static byte[] Key(string base64) => Convert.FromBase64String(base64);
}
