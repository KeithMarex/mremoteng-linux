using System.ComponentModel;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class ProgramLauncherTests
{
    private readonly List<SessionPlan> _started = [];
    private ProgramLauncher _launcher = null!;

    [SetUp]
    public void Setup()
    {
        _started.Clear();
        _launcher = new ProgramLauncher(new SessionPrograms
        {
            Find = program => "/usr/bin/" + program,
            StartDetached = plan =>
            {
                _started.Add(plan);
                return Task.FromResult(0);
            }
        });
    }

    [TestCase(ProtocolType.HTTP, "intranet", 80, "http://intranet/")]
    [TestCase(ProtocolType.HTTPS, "intranet", 443, "https://intranet/")]
    [TestCase(ProtocolType.HTTP, "intranet", 8080, "http://intranet:8080/")]
    [TestCase(ProtocolType.HTTP, "intranet/app?x=1", 8080, "http://intranet:8080/app?x=1")]
    [TestCase(ProtocolType.HTTPS, "https://intranet/app", 443, "https://intranet/app")]
    [TestCase(ProtocolType.HTTPS, "http://intranet", 443, "http://intranet/")]
    [TestCase(ProtocolType.HTTP, "http://intranet:9000/x", 8080, "http://intranet:9000/x")]
    [TestCase(ProtocolType.HTTPS, " portal.example.com ", 0, "https://portal.example.com/")]
    public void AWebConnectionOpensItsAddressInTheBrowser(ProtocolType protocol, string hostname, int port, string expected)
    {
        ConnectionInfo connection = new() { Protocol = protocol, Hostname = hostname, Port = port };

        (SessionPlan plan, _) = _launcher.Launch(connection);

        Assert.That((plan.Program, plan.Arguments), Is.EqualTo(("/usr/bin/xdg-open", (IReadOnlyList<string>)[expected])));
        Assert.That(_started.Single(), Is.SameAs(plan));
    }

    [TestCase("file:///etc/passwd")]
    [TestCase("javascript:alert(1)")]
    [TestCase("ftp://files.example.com")]
    [TestCase("smb://server/share")]
    [TestCase("")]
    public void OnlyWebAddressesGoToTheBrowser(string hostname)
    {
        ConnectionInfo connection = new() { Protocol = ProtocolType.HTTP, Hostname = hostname };

        Assert.Throws<SessionException>(() => _launcher.Launch(connection));
        Assert.That(_started, Is.Empty);
    }

    [Test]
    public void AnyDeskGetsTheIdAndTheStoredPasswordOnItsInput()
    {
        ConnectionInfo connection = new() { Protocol = ProtocolType.AnyDesk, Hostname = "123456789", Password = "secret" };

        (SessionPlan plan, _) = _launcher.Launch(connection);

        Assert.That((plan.Program, plan.Arguments), Is.EqualTo(("/usr/bin/anydesk", (IReadOnlyList<string>)["123456789", "--with-password"])));
        Assert.That(plan.Password, Is.EqualTo("secret"));
        Assert.That(plan.ToString(), Does.Not.Contain("secret"));
    }

    [Test]
    public void AnyDeskWithoutAPasswordGetsOnlyTheId()
    {
        ConnectionInfo connection = new() { Protocol = ProtocolType.AnyDesk, Hostname = "office@ad" };

        (SessionPlan plan, _) = _launcher.Launch(connection);

        Assert.That((plan.Arguments, plan.Password), Is.EqualTo(((IReadOnlyList<string>)["office@ad"], (string?)null)));
    }

    [TestCase("-123")]
    [TestCase("123 456")]
    [TestCase("123;rm")]
    [TestCase("")]
    public void AnInvalidAnyDeskIdIsRefused(string id)
    {
        ConnectionInfo connection = new() { Protocol = ProtocolType.AnyDesk, Hostname = id };

        Assert.Throws<SessionException>(() => _launcher.Launch(connection));
        Assert.That(_started, Is.Empty);
    }

    [Test]
    public void AProgramThatCannotStartIsReported()
    {
        ProgramLauncher launcher = new(new SessionPrograms
        {
            Find = program => "/usr/bin/" + program,
            StartDetached = _ => throw new Win32Exception("Permission denied")
        });

        SessionException error = Assert.Throws<SessionException>(() =>
            launcher.Launch(new ConnectionInfo { Protocol = ProtocolType.HTTPS, Hostname = "intranet" }))!;

        Assert.That(error.Message, Does.Contain("xdg-open").And.Contain("Permission denied"));
    }

    [Test]
    public void AMissingBrowserNamesThePackageToInstall()
    {
        ProgramLauncher launcher = new(new SessionPrograms { Find = _ => null });

        SessionException error = Assert.Throws<SessionException>(() =>
            launcher.Launch(new ConnectionInfo { Protocol = ProtocolType.HTTP, Hostname = "intranet" }))!;

        Assert.That(error.Message, Does.Contain("xdg-utils"));
    }

    [Test]
    public async Task TheProgramReallyStartsWithThePasswordOnItsInput()
    {
        SessionPlan plan = new("/bin/sh", ["-c", "read line; test \"$line\" = secret"]) { Password = "secret" };

        Assert.That(await ProgramLauncher.Start(plan), Is.EqualTo(0));
    }
}
