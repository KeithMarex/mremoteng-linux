using mRemoteNG.FileTransfer.Sftp;
using NUnit.Framework;

namespace mRemoteNG.FileTransfer.Tests;

public class OpenSshConfigTests
{
    [Test]
    public void TheResolvedSettingsAreRead()
    {
        const string output = """
            User deploy
            hostname 10.0.0.5
            port 2222
            hashknownhosts yes
            stricthostkeychecking accept-new
            identityfile ~/.ssh/id_ed25519
            identityfile /keys/work_rsa
            globalknownhostsfile /etc/ssh/ssh_known_hosts /etc/ssh/ssh_known_hosts2
            userknownhostsfile /home/u/.ssh/known_hosts /home/u/.ssh/known_hosts2
            """;

        SshHostConfig config = OpenSshConfig.Parse(output, "/home/u")!;

        Assert.That((config.HostName, config.Port, config.User), Is.EqualTo(("10.0.0.5", 2222, "deploy")));
        Assert.That(config.IdentityFiles, Is.EqualTo(new[] { "/home/u/.ssh/id_ed25519", "/keys/work_rsa" }));
        Assert.That(config.UserKnownHostsFiles, Is.EqualTo(new[] { "/home/u/.ssh/known_hosts", "/home/u/.ssh/known_hosts2" }));
        Assert.That(config.GlobalKnownHostsFiles, Has.Count.EqualTo(2));
        Assert.That((config.HashKnownHosts, config.StrictHostKeyChecking, config.Proxy), Is.EqualTo((true, "accept-new", (string?)null)));
    }

    [TestCase("proxyjump bastion", "ProxyJump bastion")]
    [TestCase("proxycommand nc %h %p", "ProxyCommand nc %h %p")]
    [TestCase("proxyjump none", null)]
    public void AProxyIsReportedBecauseSftpCannotFollowIt(string line, string? proxy)
    {
        Assert.That(OpenSshConfig.Parse($"hostname h\nport 22\n{line}\n", "/home/u")!.Proxy, Is.EqualTo(proxy));
    }

    [TestCase("true", "yes")]
    [TestCase("false", "no")]
    [TestCase("ask", "ask")]
    public void StrictHostKeyCheckingIsNormalized(string value, string expected)
    {
        Assert.That(OpenSshConfig.Parse($"hostname h\nport 22\nstricthostkeychecking {value}\n", "/home/u")!.StrictHostKeyChecking,
                    Is.EqualTo(expected));
    }

    [Test]
    public void OutputWithoutHostIsNotUsed()
    {
        Assert.That(OpenSshConfig.Parse("garbage", "/home/u"), Is.Null);
    }
}

public class RemotePathTests
{
    [TestCase("/", "a", "/a")]
    [TestCase("/srv", "a", "/srv/a")]
    [TestCase("/srv/", "a", "/srv/a")]
    public void Combine(string directory, string name, string expected) => Assert.That(RemotePath.Combine(directory, name), Is.EqualTo(expected));

    [TestCase("/srv/www", "/srv")]
    [TestCase("/srv/www/", "/srv")]
    [TestCase("/srv", "/")]
    [TestCase("/", "/")]
    public void Parent(string path, string expected) => Assert.That(RemotePath.Parent(path), Is.EqualTo(expected));

    [TestCase("report.pdf", true)]
    [TestCase(".bashrc", true)]
    [TestCase("..", false)]
    [TestCase(".", false)]
    [TestCase("../.bashrc", false)]
    [TestCase("a/b", false)]
    [TestCase("", false)]
    public void OnlyPlainNamesFromAServerAreUsed(string name, bool safe) => Assert.That(RemotePath.IsSafeName(name), Is.EqualTo(safe));

    [Test]
    public void ADownloadNeverLandsOutsideItsDirectory()
    {
        Assert.That(RemotePath.LocalTarget("/home/u/Downloads", "report.pdf"), Is.EqualTo("/home/u/Downloads/report.pdf"));
        Assert.Throws<InvalidDataException>(() => RemotePath.LocalTarget("/home/u/Downloads", "../.bashrc"));
        Assert.Throws<InvalidDataException>(() => RemotePath.LocalTarget("/home/u/Downloads", "/etc/passwd"));
    }
}
