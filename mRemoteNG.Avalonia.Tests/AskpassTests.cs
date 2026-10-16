using System.Diagnostics;
using System.Runtime.Versioning;
using mRemoteNG.Avalonia.Sessions.Askpass;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class AskpassTests
{
    private const string Prompt = "admin@db1's password: ";

    private TempDirectory _directory = null!;

    [SetUp]
    public void Setup() => _directory = new TempDirectory();

    [TearDown]
    public void Teardown() => _directory.Dispose();

    [Test]
    public void ThePasswordIsGivenOnceToTheHostsPasswordPrompt()
    {
        using AskpassServer server = Start("s3cret", "db1");

        Assert.That(Ask(server, "admin@jump's password: "), Is.EqualTo(AskpassReply.UseTerminal));
        Assert.That(Ask(server, Prompt), Is.EqualTo(new AskpassReply("s3cret", false)));
        // A second prompt means the password was wrong; the user types the next attempt.
        Assert.That(Ask(server, Prompt), Is.EqualTo(AskpassReply.UseTerminal));
    }

    [Test]
    public void AHelperWithoutTheSessionsTokenGetsNoPassword()
    {
        using AskpassServer server = Start("s3cret", "db1");

        AskpassReply? reply = AskpassClient.AskServer(Environment(server, token: "guess"), Prompt);

        Assert.That(reply, Is.EqualTo(AskpassReply.UseTerminal));
        Assert.That(Ask(server, Prompt).Password, Is.EqualTo("s3cret"));
    }

    [Test]
    [SupportedOSPlatform("linux")]
    public void TheSocketIsPrivateAndRemovedWhenTheSessionEnds()
    {
        string socketPath;
        using (AskpassServer server = Start("s3cret", "db1"))
        {
            socketPath = server.SocketPath;
            Assert.That(File.GetUnixFileMode(socketPath), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            Assert.That(File.GetUnixFileMode(Path.GetDirectoryName(socketPath)!),
                        Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
            Assert.That(server.Environment.Values, Has.None.Contains("s3cret"));
        }

        Assert.That(File.Exists(socketPath), Is.False);
        Assert.That(AskpassClient.AskServer(name => name == AskpassServer.SocketVariable ? socketPath : null, Prompt), Is.Null);
    }

    [Test]
    public void SshIsToldToUseTheHelper()
    {
        using AskpassServer server = Start("s3cret", "db1");

        Assert.That(server.Environment["SSH_ASKPASS"], Is.EqualTo("/usr/lib/mremoteng/mRemoteNG.Avalonia"));
        Assert.That(server.Environment["SSH_ASKPASS_REQUIRE"], Is.EqualTo("force"));
    }

    [Test]
    public void TheAppsExecutableActsAsTheHelperWhenSshStartsIt()
    {
        string helper = Path.Combine(AppContext.BaseDirectory, "mRemoteNG.Avalonia");
        using AskpassServer server = AskpassServer.Start(_directory.File("run"), helper, "s3cret", "db1");
        ProcessStartInfo start = new(helper, [Prompt]) { RedirectStandardOutput = true, UseShellExecute = false };
        foreach ((string name, string value) in server.Environment)
            start.Environment[name] = value;

        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        Assert.That(process.WaitForExit(30_000), Is.True);

        Assert.That((process.ExitCode, output), Is.EqualTo((0, "s3cret\n")));
    }

    private AskpassServer Start(string password, string? host) =>
        AskpassServer.Start(_directory.File("run"), "/usr/lib/mremoteng/mRemoteNG.Avalonia", password, host);

    private static AskpassReply Ask(AskpassServer server, string prompt) => AskpassClient.AskServer(Environment(server), prompt)!;

    private static Func<string, string?> Environment(AskpassServer server, string? token = null) =>
        name => name == AskpassServer.TokenVariable && token is not null ? token : server.Environment.GetValueOrDefault(name);
}
