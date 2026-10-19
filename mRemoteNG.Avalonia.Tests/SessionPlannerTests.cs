using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class SessionPlannerTests
{
    private const string App = "/usr/lib/mremoteng/mRemoteNG.Avalonia";

    private static readonly SessionPrograms Programs = new()
    {
        Shell = "/bin/bash",
        App = App,
        Find = program => program.StartsWith('/') ? program : "/usr/bin/" + program
    };

    private readonly SessionPlanner _planner = new(Programs);

    [Test]
    public void SshConnectsToTheHostWithTheUserName()
    {
        SessionPlan plan = _planner.Plan(Ssh(hostname: "db1.example.com", username: "admin"));

        Assert.That(plan.Program, Is.EqualTo("/usr/bin/ssh"));
        Assert.That(plan.Arguments, Is.EqualTo(new[] { "-l", "admin", "--", "db1.example.com" }));
        Assert.That(plan.Warnings, Is.Empty);
    }

    [Test]
    public void OnlyANonDefaultPortIsPassed()
    {
        Assert.That(_planner.Plan(Ssh(port: 2222)).Arguments, Is.EqualTo(new[] { "-p", "2222", "--", "host" }));
        Assert.That(_planner.Plan(Ssh(port: 22)).Arguments, Is.EqualTo(new[] { "--", "host" }));
    }

    [TestCase("-oProxyCommand=touch /tmp/pwned")]
    [TestCase("-oProxyCommand=x")]
    [TestCase("host name")]
    [TestCase("host\nname")]
    public void AHostNameThatCouldBeAnOptionIsRefusedWithoutEchoingIt(string hostname)
    {
        SessionException ex = Assert.Throws<SessionException>(() => _planner.Plan(Ssh(hostname: hostname)))!;

        Assert.That(ex.Message, Is.EqualTo(UiText.UnsafeHostname));
    }

    [Test]
    public void AUserNameThatCouldBeAnOptionIsRefused()
    {
        Assert.Throws<SessionException>(() => _planner.Plan(Ssh(username: "-oProxyCommand=x")));
    }

    [Test]
    public void PuttyOptionsWithTheSameMeaningArePassedOnAndTheRestReportedWithoutTheirValues()
    {
        ConnectionInfo connection = Ssh();
        connection.SSHOptions = "-L 8080:localhost:80 -A -pw secret -proxycmd \"nc evil 22\" -2 -D 1080 -o ProxyCommand=x";

        SessionPlan plan = _planner.Plan(connection);

        Assert.That(plan.Arguments, Is.EqualTo(new[] { "-L", "8080:localhost:80", "-A", "-D", "1080", "--", "host" }));
        Assert.That(plan.Warnings, Has.Count.EqualTo(3));
        Assert.That(string.Join(" ", plan.Warnings), Does.Contain("-pw").And.Contain("-proxycmd").And.Contain("-o"));
        Assert.That(string.Join(" ", plan.Warnings), Does.Not.Contain("secret").And.Not.Contain("evil").And.Not.Contain("ProxyCommand"));
    }

    [Test]
    public void APortForwardWithAnUnsafeValueIsLeftOut()
    {
        ConnectionInfo connection = Ssh();
        connection.SSHOptions = "-L -oProxyCommand=x";

        Assert.That(_planner.Plan(connection).Arguments, Is.EqualTo(new[] { "--", "host" }));
    }

    [Test]
    public void APuttySessionBecomesAnSshConfigHostWhileTheHostNameStillDecides()
    {
        ConnectionInfo connection = Ssh(hostname: "10.0.0.5");
        connection.PuttySession = "prod-bastion";

        Assert.That(_planner.Plan(connection).Arguments, Is.EqualTo(new[] { "-o", "HostName=10.0.0.5", "--", "prod-bastion" }));
    }

    [Test]
    public void APuttySessionWithoutHostNameConnectsToTheSshConfigHost()
    {
        ConnectionInfo connection = Ssh(hostname: "");
        connection.PuttySession = "prod-bastion";

        Assert.That(_planner.Plan(connection).Arguments, Is.EqualTo(new[] { "--", "prod-bastion" }));
    }

    [Test]
    public void TheDefaultPuttySessionIsNoHost()
    {
        ConnectionInfo connection = Ssh();
        connection.PuttySession = "Default Settings";

        Assert.That(_planner.Plan(connection).Arguments, Is.EqualTo(new[] { "--", "host" }));
    }

    [Test]
    public void TheStoredPasswordIsKeptForTheAskpassHelperAndNeverOnTheCommandLine()
    {
        ConnectionInfo connection = Ssh(hostname: "admin@DB1");
        connection.Password = "s3cret";

        SessionPlan plan = _planner.Plan(connection);

        Assert.That(plan.Password, Is.EqualTo("s3cret"));
        Assert.That(plan.PasswordHost, Is.EqualTo("DB1"));
        Assert.That(plan.Arguments, Has.None.Contains("s3cret"));
        Assert.That(plan.ToString(), Does.Not.Contain("s3cret"));
    }

    [Test]
    public void AConnectionWithoutHostNameCannotBeOpened()
    {
        Assert.Throws<SessionException>(() => _planner.Plan(Ssh(hostname: " ")));
    }

    [Test]
    public void Ssh1IsRefusedWithAnExplanation()
    {
        ConnectionInfo connection = Ssh();
        connection.Protocol = ProtocolType.SSH1;

        Assert.That(Assert.Throws<SessionException>(() => _planner.Plan(connection))!.Message, Is.EqualTo(UiText.Ssh1NotSupported));
    }

    [Test]
    public void TelnetConnectsWithoutTheCredentials()
    {
        ConnectionInfo connection = new() { Protocol = ProtocolType.Telnet, Hostname = "switch1", Username = "admin", Password = "pw" };
        connection.SetDefaultPort(); // as the property panel does when the protocol changes

        SessionPlan plan = _planner.Plan(connection);

        Assert.That((plan.Program, plan.Arguments), Is.EqualTo(("/usr/bin/telnet", (IReadOnlyList<string>)["--", "switch1", "23"])));
        Assert.That(plan.Password, Is.Null);
    }

    [Test]
    public void RawRunsThisAppAsRelay()
    {
        SessionPlan plan = _planner.Plan(new ConnectionInfo { Protocol = ProtocolType.RAW, Hostname = "printer", Port = 9100 });

        Assert.That((plan.Program, plan.Arguments), Is.EqualTo((App, (IReadOnlyList<string>)[RawTcpRelay.Argument, "printer", "9100"])));
        Assert.That(plan.ToString(), Is.EqualTo("raw TCP printer:9100"));
    }

    [Test]
    public void RawNeedsTheAppsOwnExecutable()
    {
        SessionPlanner planner = new(new SessionPrograms { App = null, Find = Programs.Find });

        Assert.Throws<SessionException>(() => planner.Plan(new ConnectionInfo { Protocol = ProtocolType.RAW, Hostname = "printer" }));
    }

    [TestCase("")]
    [TestCase("localhost")]
    public void ALocalTerminalOpensTheUsersShell(string hostname)
    {
        SessionPlan plan = _planner.Plan(new ConnectionInfo { Protocol = ProtocolType.Terminal, Hostname = hostname });

        Assert.That((plan.Program, plan.Arguments), Is.EqualTo(("/bin/bash", (IReadOnlyList<string>)[])));
    }

    [Test]
    public void ARemoteTerminalUsesSsh()
    {
        ConnectionInfo connection = new() { Protocol = ProtocolType.Terminal, Hostname = "web1", Username = "deploy" };
        connection.SetDefaultPort();

        SessionPlan plan = _planner.Plan(connection);

        Assert.That((plan.Program, plan.Arguments), Is.EqualTo(("/usr/bin/ssh", (IReadOnlyList<string>)["-l", "deploy", "--", "web1"])));
    }

    [TestCase(513, new[] { "--", "oldhost" })]
    [TestCase(5513, new[] { "-p", "5513", "--", "oldhost" })]
    public void RloginUsesTheRloginClient(int port, string[] arguments)
    {
        SessionPlan plan = _planner.Plan(new ConnectionInfo { Protocol = ProtocolType.Rlogin, Hostname = "oldhost", Port = port });

        Assert.That((plan.Program, plan.Arguments), Is.EqualTo(("/usr/bin/rlogin", (IReadOnlyList<string>)arguments)));
        Assert.That(plan.Warnings, Is.Empty);
    }

    [Test]
    public void WithoutAnRloginClientThisAppRelaysAndSaysWhatThatMeans()
    {
        SessionPlanner planner = new(new SessionPrograms { App = App, Find = program => program == "rlogin" ? null : Programs.Find(program) });
        ConnectionInfo connection = new() { Protocol = ProtocolType.Rlogin, Hostname = "oldhost" };
        connection.SetDefaultPort();

        SessionPlan plan = planner.Plan(connection);

        Assert.That((plan.Program, plan.Arguments), Is.EqualTo((App, (IReadOnlyList<string>)[RloginRelay.Argument, "oldhost", "513"])));
        Assert.That(plan.ToString(), Is.EqualTo("rlogin oldhost:513"));
        Assert.That(plan.Warnings, Is.EqualTo(new[] { UiText.NoRloginClient }));
    }

    [TestCase("")]
    [TestCase("localhost")]
    public void ALocalPowerShellOpensPwsh(string hostname)
    {
        SessionPlan plan = _planner.Plan(new ConnectionInfo { Protocol = ProtocolType.PowerShell, Hostname = hostname });

        Assert.That((plan.Program, plan.Arguments), Is.EqualTo(("/usr/bin/pwsh", (IReadOnlyList<string>)["-NoLogo"])));
    }

    [Test]
    public void RemotePowerShellEntersASessionOverSshWithTheDomainUser()
    {
        ConnectionInfo connection = new()
        {
            Protocol = ProtocolType.PowerShell, Hostname = "srv1", Username = "admin", Domain = "CORP", Password = "secret", Port = 2222
        };

        SessionPlan plan = _planner.Plan(connection);

        Assert.That(plan.Program, Is.EqualTo("/usr/bin/pwsh"));
        Assert.That(plan.Arguments, Is.EqualTo(new[]
        {
            "-NoLogo", "-NoExit", "-Command", @"Enter-PSSession -HostName 'srv1' -UserName 'CORP\admin' -Port 2222"
        }));
        Assert.That((plan.Password, plan.PasswordHost), Is.EqualTo(("secret", "srv1")));
        Assert.That(plan.ToString(), Does.Not.Contain("secret"));
        Assert.That(plan.Warnings, Is.Empty);
    }

    [Test]
    public void TheWinRmPortIsLeftOutWithAnExplanation()
    {
        ConnectionInfo connection = new() { Protocol = ProtocolType.PowerShell, Hostname = "srv1" };
        connection.SetDefaultPort();

        SessionPlan plan = _planner.Plan(connection);

        Assert.That(plan.Arguments[^1], Is.EqualTo("Enter-PSSession -HostName 'srv1'"));
        Assert.That(plan.Warnings.Single(), Does.Contain("5985"));
    }

    [TestCase("ad'min")]
    [TestCase("ad’min")]
    [TestCase("-admin")]
    [TestCase("ad min")]
    public void APowerShellUserNameThatCouldEndTheQuotesIsRefused(string username)
    {
        ConnectionInfo connection = new() { Protocol = ProtocolType.PowerShell, Hostname = "srv1", Username = username };

        Assert.Throws<SessionException>(() => _planner.Plan(connection));
    }

    [Test]
    public void APowerShellHostNameThatCouldEndTheQuotesIsRefused()
    {
        ConnectionInfo connection = new() { Protocol = ProtocolType.PowerShell, Hostname = "srv'1" };

        Assert.Throws<SessionException>(() => _planner.Plan(connection));
    }

    [Test]
    public void WslIsRefusedWithAnExplanation()
    {
        ConnectionInfo connection = new() { Protocol = ProtocolType.WSL, Hostname = "Ubuntu" };

        Assert.That(ProtocolRouting.KindOf(ProtocolType.WSL), Is.EqualTo(SessionKind.NotSupported));
        Assert.That(Assert.Throws<SessionException>(() => _planner.Plan(connection))!.Message, Does.Contain("only on Windows"));
    }

    [TestCase(ProtocolType.SSH2, SessionKind.Terminal)]
    [TestCase(ProtocolType.SSH1, SessionKind.Terminal)]
    [TestCase(ProtocolType.Rlogin, SessionKind.Terminal)]
    [TestCase(ProtocolType.PowerShell, SessionKind.Terminal)]
    [TestCase(ProtocolType.SFTP, SessionKind.FileTransfer)]
    [TestCase(ProtocolType.RDP, SessionKind.Rdp)]
    [TestCase(ProtocolType.HTTPS, SessionKind.Launch)]
    [TestCase(ProtocolType.AnyDesk, SessionKind.Launch)]
    [TestCase(ProtocolType.WSL, SessionKind.NotSupported)]
    public void EachProtocolOpensInItsKindOfSession(ProtocolType protocol, SessionKind kind)
    {
        Assert.That(ProtocolRouting.KindOf(protocol), Is.EqualTo(kind));
    }

    [Test]
    public void AMissingProgramNamesThePackageToInstall()
    {
        SessionPlanner planner = new(new SessionPrograms { Find = _ => null });

        Assert.That(Assert.Throws<SessionException>(() => planner.Plan(Ssh()))!.Message, Does.Contain("openssh-client"));
    }

    [TestCase("admin@db1's password: ", "db1", true)]
    [TestCase("admin@DB1's password: ", "db1", true)]
    [TestCase("admin@jump's password: ", "db1", false)]
    [TestCase("(admin@db1) Password: ", "db1", true)]
    [TestCase("(admin@jump) Password: ", "db1", false)]
    [TestCase("Password: ", "db1", true)]
    [TestCase("admin@jump's password: ", null, true)]
    [TestCase("Enter passphrase for key '/home/u/.ssh/id_ed25519': ", "db1", false)]
    [TestCase("Are you sure you want to continue connecting (yes/no/[fingerprint])? ", "db1", false)]
    [TestCase("Verification code: ", "db1", false)]
    public void OnlyThePasswordPromptOfTheConnectionsHostGetsTheStoredPassword(string prompt, string? host, bool expected)
    {
        Assert.That(OpenSshCommand.IsPasswordPromptFor(prompt, host), Is.EqualTo(expected));
    }

    [TestCase("/usr/lib/mremoteng/mRemoteNG.Avalonia", "/usr/lib/mremoteng/mRemoteNG.Avalonia")]
    [TestCase("/usr/bin/dotnet", null)]
    [TestCase(null, null)]
    public void OnlyTheAppsOwnExecutableCanBeTheHelper(string? processPath, string? expected)
    {
        Assert.That(SessionPrograms.AppExecutable(processPath), Is.EqualTo(expected));
    }

    private static ConnectionInfo Ssh(string hostname = "host", string username = "", int port = 22) =>
        new() { Protocol = ProtocolType.SSH2, Hostname = hostname, Username = username, Port = port };
}
