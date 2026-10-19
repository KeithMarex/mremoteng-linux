using mRemoteNG.Avalonia.Services;
using mRemoteNG.Avalonia.Sessions;
using mRemoteNG.Avalonia.Sessions.Rdp;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Connection.Protocol.RDP;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class FreeRdpCommandTests
{
    private const string Program = "/usr/bin/xfreerdp3";
    private static readonly RdpDisplay InTab = new(ParentWindow: 4194322, Width: 1000, Height: 700, ScreenWidth: 1920, ScreenHeight: 1080);
    private static readonly LoginCredentials Login = new("admin", "CORP", "secret");

    [Test]
    public void TheSessionIsEmbeddedInTheTabAtItsSize()
    {
        FreeRdpPlan plan = Build(Rdp());

        Assert.That(plan.Program, Is.EqualTo(Program));
        Assert.That(plan.Arguments, Is.SupersetOf(new[]
        {
            "/v:srv1", "/u:admin", "/d:CORP", "/p:secret", "/parent-window:4194322", "/size:1000x700", "/dynamic-resolution",
            "/bpp:16", "/cert:ignore", "-grab-keyboard", "+auto-reconnect"
        }));
        Assert.That(plan.Arguments, Has.None.StartsWith("/port:"));
        Assert.That(plan.OwnWindow, Is.False);
    }

    [Test]
    public void ANonDefaultPortIsPassed()
    {
        ConnectionInfo connection = Rdp();
        connection.Port = 3390;

        Assert.That(Build(connection).Arguments, Does.Contain("/port:3390"));
    }

    [Test]
    public void SmartSizeScalesAScreenSizedDesktopIntoTheTab()
    {
        ConnectionInfo connection = Rdp();
        connection.Resolution = RDPResolutions.SmartSize;

        FreeRdpPlan plan = Build(connection);

        Assert.That(plan.Arguments, Is.SupersetOf(new[] { "/size:1920x1080", "/smart-sizing", "/parent-window:4194322" }));
        Assert.That(plan.Arguments, Does.Not.Contain("/dynamic-resolution"));
    }

    [Test]
    public void FullScreenOpensAWindowOfItsOwn()
    {
        ConnectionInfo connection = Rdp();
        connection.Resolution = RDPResolutions.Fullscreen;

        FreeRdpPlan plan = Build(connection);

        Assert.That(plan.Arguments, Is.SupersetOf(new[] { "/f", "/wm-class:mremoteng", "/title:Server 1" }));
        Assert.That(plan.Arguments, Has.None.StartsWith("/parent-window:"));
        Assert.That(plan.OwnWindow, Is.True);
    }

    [Test]
    public void WithoutAWindowToEmbedInTheSessionGetsItsOwn()
    {
        FreeRdpPlan plan = FreeRdpCommand.Build(Rdp(), InTab with { ParentWindow = null }, Login, "ignore", Program);

        Assert.That(plan.OwnWindow, Is.True);
        Assert.That(plan.Arguments, Has.None.StartsWith("/parent-window:"));
    }

    [Test]
    public void AFixedSizeIsKeptWithoutAutomaticResize()
    {
        ConnectionInfo connection = Rdp();
        connection.AutomaticResize = false;

        Assert.That(Build(connection).Arguments, Does.Not.Contain("/dynamic-resolution"));
    }

    [TestCase(RDPDiskDrives.None, null)]
    [TestCase(RDPDiskDrives.Local, "+home-drive")]
    [TestCase(RDPDiskDrives.All, "+drives")]
    [TestCase(RDPDiskDrives.Custom, "+home-drive")]
    public void DrivesAreSharedAsLinuxFolders(RDPDiskDrives drives, string? expected)
    {
        ConnectionInfo connection = Rdp();
        connection.RedirectDiskDrives = drives;

        FreeRdpPlan plan = Build(connection);

        Assert.That(plan.Arguments.Where(a => a is "+home-drive" or "+drives"), Is.EqualTo(expected is null ? Array.Empty<string>() : new[] { expected }));
        Assert.That(plan.Warnings.Any(), Is.EqualTo(drives == RDPDiskDrives.Custom));
    }

    [Test]
    public void DevicesClipboardAndSoundAreRedirectedAsSet()
    {
        ConnectionInfo connection = Rdp();
        connection.RedirectPrinters = true;
        connection.RedirectSmartCards = true;
        connection.RedirectClipboard = true;
        connection.RedirectSound = RDPSounds.BringToThisComputer;
        connection.SoundQuality = RDPSoundQuality.High;
        connection.RedirectAudioCapture = true;
        connection.RedirectKeys = true;

        FreeRdpPlan plan = Build(connection);

        Assert.That(plan.Arguments, Is.SupersetOf(new[]
        {
            "/printer", "/smartcard", "+clipboard", "/sound:quality:high", "/microphone", "+grab-keyboard"
        }));
    }

    [TestCase(RDPSounds.LeaveAtRemoteComputer, "/audio-mode:1")]
    [TestCase(RDPSounds.DoNotPlay, "/audio-mode:2")]
    public void SoundCanStayOnTheServerOrBeOff(RDPSounds sound, string expected)
    {
        ConnectionInfo connection = Rdp();
        connection.RedirectSound = sound;

        Assert.That(Build(connection).Arguments, Does.Contain(expected));
    }

    [Test]
    public void SerialPortsAreReportedAsNotRedirected()
    {
        ConnectionInfo connection = Rdp();
        connection.RedirectPorts = true;

        Assert.That(Build(connection).Warnings.Single(), Does.Contain("serial"));
    }

    [Test]
    public void TheDesktopExperienceFollowsTheConnection()
    {
        ConnectionInfo connection = Rdp();
        connection.DisplayWallpaper = true;
        connection.EnableFontSmoothing = true;
        connection.DisableFullWindowDrag = true;
        connection.CacheBitmaps = true;

        FreeRdpPlan plan = Build(connection);

        Assert.That(plan.Arguments, Is.SupersetOf(new[]
        {
            "+wallpaper", "-themes", "+fonts", "-aero", "-window-drag", "+menu-anims", "/cache:persist"
        }));
    }

    [Test]
    public void SecurityOptionsAreMapped()
    {
        ConnectionInfo connection = Rdp();
        connection.UseCredSsp = false;
        connection.UseConsoleSession = true;
        connection.UseRestrictedAdmin = true;
        connection.UseRCG = true;

        Assert.That(Build(connection).Arguments, Is.SupersetOf(new[] { "/sec:nla:off", "/admin", "/restricted-admin", "/remoteGuard" }));
    }

    [Test]
    public void AHyperVConsoleConnectsToTheVirtualMachine()
    {
        ConnectionInfo connection = Rdp();
        connection.UseVmId = true;
        connection.VmId = "0d5e6f7a-1b2c-4d3e-8f90-a1b2c3d4e5f6";

        Assert.That(Build(connection).Arguments, Does.Contain("/vmconnect:0d5e6f7a-1b2c-4d3e-8f90-a1b2c3d4e5f6"));
    }

    [Test]
    public void TheStartProgramAndLoadBalanceInfoArePassed()
    {
        ConnectionInfo connection = Rdp();
        connection.RDPStartProgram = @"C:\Tools\app.exe";
        connection.RDPStartProgramWorkDir = @"C:\Tools";
        connection.LoadBalanceInfo = "tsv://MS Terminal Services Plugin.1.Pool";

        Assert.That(Build(connection).Arguments, Is.SupersetOf(new[]
        {
            @"/shell:C:\Tools\app.exe", @"/shell-dir:C:\Tools", "/load-balance-info:tsv://MS Terminal Services Plugin.1.Pool"
        }));
    }

    [Test]
    public void TheGatewayUsesTheConnectionsLoginByDefault()
    {
        ConnectionInfo connection = Rdp();
        connection.RDGatewayUsageMethod = RDGatewayUsageMethod.Always;
        connection.RDGatewayHostname = "gw.example.com";

        Assert.That(Build(connection).Arguments, Does.Contain("/gateway:g:gw.example.com,u:admin,d:CORP,p:secret,usage-method:direct"));
    }

    [Test]
    public void TheGatewayCanHaveItsOwnLogin()
    {
        ConnectionInfo connection = Rdp();
        connection.RDGatewayUsageMethod = RDGatewayUsageMethod.Detect;
        connection.RDGatewayHostname = "gw.example.com";
        connection.RDGatewayUseConnectionCredentials = RDGatewayUseConnectionCredentials.No;
        connection.RDGatewayUsername = "gwuser";
        connection.RDGatewayPassword = "gwpass";

        Assert.That(Build(connection).Arguments, Does.Contain("/gateway:g:gw.example.com,u:gwuser,p:gwpass,usage-method:detect"));
    }

    [Test]
    public void TheGatewayCanUseAnAccessToken()
    {
        ConnectionInfo connection = Rdp();
        connection.RDGatewayUsageMethod = RDGatewayUsageMethod.Always;
        connection.RDGatewayHostname = "gw.example.com";
        connection.RDGatewayUseConnectionCredentials = RDGatewayUseConnectionCredentials.AccessToken;
        connection.RDGatewayAccessToken = "token123";

        FreeRdpPlan plan = Build(connection);

        Assert.That(plan.Arguments, Does.Contain("/gateway:g:gw.example.com,access-token:token123,usage-method:direct"));
        Assert.That(plan.ToString(), Does.Contain("access-token:***").And.Not.Contain("token123"));
    }

    [Test]
    public void AGatewayValueWithACommaIsRefused()
    {
        ConnectionInfo connection = Rdp();
        connection.RDGatewayUsageMethod = RDGatewayUsageMethod.Always;
        connection.RDGatewayHostname = "gw.example.com";

        Assert.Throws<SessionException>(() =>
            FreeRdpCommand.Build(connection, InTab, Login with { Password = "a,b" }, "ignore", Program));
    }

    [Test]
    public void AGatewayWithoutHostIsLeftOutWithAWarning()
    {
        ConnectionInfo connection = Rdp();
        connection.RDGatewayUsageMethod = RDGatewayUsageMethod.Always;

        FreeRdpPlan plan = Build(connection);

        Assert.That(plan.Arguments, Has.None.StartsWith("/gateway:"));
        Assert.That(plan.Warnings, Has.Some.Contains("Gateway"));
    }

    [Test]
    public void AValueWithALineBreakIsRefused()
    {
        Assert.Throws<SessionException>(() => FreeRdpCommand.Build(Rdp(), InTab, Login with { Password = "a\nb" }, "ignore", Program));
    }

    [Test]
    public void AHostNameThatCouldBeAnOptionIsRefused()
    {
        ConnectionInfo connection = Rdp();
        connection.Hostname = "-x";

        Assert.Throws<SessionException>(() => Build(connection));
    }

    [Test]
    public void ThePasswordsNeverShowInTheDescription()
    {
        ConnectionInfo connection = Rdp();
        connection.RDGatewayUsageMethod = RDGatewayUsageMethod.Always;
        connection.RDGatewayHostname = "gw.example.com";

        string shown = Build(connection).ToString();

        Assert.That(shown, Does.Not.Contain("secret"));
        Assert.That(shown, Does.StartWith("xfreerdp3 /v:srv1").And.Contain("/p:***").And.Contain("p:***"));
    }

    private static FreeRdpPlan Build(ConnectionInfo connection) => FreeRdpCommand.Build(connection, InTab, Login, "ignore", Program);

    private static ConnectionInfo Rdp()
    {
        ConnectionInfo connection = new()
        {
            Name = "Server 1",
            Protocol = ProtocolType.RDP,
            Hostname = "srv1",
            Resolution = RDPResolutions.FitToWindow,
            AutomaticResize = true,
            Colors = RDPColors.Colors16Bit,
            RedirectDiskDrives = RDPDiskDrives.None,
            RedirectSound = RDPSounds.DoNotPlay
        };
        connection.SetDefaultPort();
        return connection;
    }
}
