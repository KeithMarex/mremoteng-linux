using System.Globalization;
using mRemoteNG.Avalonia.Services;
using mRemoteNG.Connection;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Connection.Protocol.RDP;

namespace mRemoteNG.Avalonia.Sessions.Rdp;

/// <summary>Where the remote desktop is shown: inside a tab (an X11 window to embed in) or in its own window.</summary>
/// <param name="ParentWindow">The X11 window xfreerdp puts its window in; null for a window of its own.</param>
/// <param name="Width">The tab's width in pixels.</param>
/// <param name="Height">The tab's height in pixels.</param>
/// <param name="ScreenWidth">The width of the screen the app is on, in pixels.</param>
/// <param name="ScreenHeight">The height of that screen.</param>
public sealed record RdpDisplay(ulong? ParentWindow, int Width, int Height, int ScreenWidth, int ScreenHeight);

/// <summary>What to run for an RDP connection. The arguments go to xfreerdp's standard input, never on its command line.</summary>
public sealed record FreeRdpPlan(string Program, IReadOnlyList<string> Arguments)
{
    /// <summary>Settings of the connection that could not be applied; they go to the notifications.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Whether the session shows in its own (full screen) window rather than in the tab.</summary>
    public bool OwnWindow { get; init; }

    /// <summary>The arguments as shown to the user, with the passwords and the access token left out.</summary>
    public override string ToString() =>
        string.Join(' ', [Path.GetFileName(Program), .. Arguments.Select(FreeRdpCommand.Redact)]);
}

/// <summary>
/// Maps an RDP connection's settings to xfreerdp (FreeRDP 3) arguments, the counterpart of the ActiveX settings the
/// Windows app makes in RdpProtocol. The arguments are written one per line to xfreerdp's standard input
/// (/args-from:stdin), so the passwords never appear on a command line.
/// </summary>
internal static class FreeRdpCommand
{
    /// <summary>The window class of xfreerdp's own window, so the desktop groups it with this app.</summary>
    private const string WindowClass = "mremoteng";

    // The Windows app's default number of reconnection attempts (Settings.RdpReconnectionCount).
    private const int ReconnectAttempts = 5;

    /// <param name="certificate">The /cert option: ignore, deny, or the fingerprints of accepted certificates.</param>
    /// <exception cref="SessionException">A setting cannot be passed to xfreerdp.</exception>
    public static FreeRdpPlan Build(ConnectionInfo connection, RdpDisplay display, LoginCredentials credentials,
                                    string certificate, string program)
    {
        List<string> warnings = [];
        List<string> arguments = ["/v:" + SessionPlanner.Host(connection)];

        int port = SessionPlanner.Port(connection);
        if (port != DefaultPorts.Rdp)
            arguments.Add("/port:" + port.ToString(CultureInfo.InvariantCulture));

        if (credentials.Username.Length > 0)
            arguments.Add("/u:" + credentials.Username);
        if (credentials.Domain.Length > 0)
            arguments.Add("/d:" + credentials.Domain);
        if (credentials.Password.Length > 0)
            arguments.Add("/p:" + credentials.Password);

        bool ownWindow = AddDisplay(connection, display, arguments);
        arguments.Add("/bpp:" + ((int)connection.Colors).ToString(CultureInfo.InvariantCulture));
        arguments.Add("/cert:" + certificate);
        AddSecurity(connection, arguments, warnings);
        AddRedirection(connection, arguments, warnings);
        AddExperience(connection, arguments);
        AddSession(connection, arguments, warnings);
        AddGateway(connection, credentials, arguments, warnings);

        arguments.Add("+auto-reconnect");
        arguments.Add("/auto-reconnect-max-retries:" + ReconnectAttempts.ToString(CultureInfo.InvariantCulture));
        // Windows key combinations go to the remote computer only when the connection says so, as on Windows.
        arguments.Add(connection.RedirectKeys ? "+grab-keyboard" : "-grab-keyboard");

        foreach (string argument in arguments)
        {
            // One argument per line, and an empty line ends them.
            if (argument.Contains('\n') || argument.Contains('\r'))
                throw new SessionException(UiText.RdpValueWithLineBreak(ArgumentName(argument)));
        }

        return new FreeRdpPlan(program, arguments) { Warnings = warnings, OwnWindow = ownWindow };
    }

    // Returns whether the session gets a window of its own.
    private static bool AddDisplay(ConnectionInfo connection, RdpDisplay display, List<string> arguments)
    {
        bool fullScreen = connection.Resolution == RDPResolutions.Fullscreen;
        if (fullScreen || display.ParentWindow is null)
        {
            arguments.Add("/wm-class:" + WindowClass);
            arguments.Add("/title:" + connection.Name);
        }
        else
        {
            arguments.Add("/parent-window:" + display.ParentWindow.Value.ToString(CultureInfo.InvariantCulture));
        }

        switch (connection.Resolution)
        {
            case RDPResolutions.Fullscreen:
                arguments.Add("/f");
                if (connection.AutomaticResize)
                    arguments.Add("/dynamic-resolution");
                return true;
            case RDPResolutions.SmartSize:
                // As on Windows: a desktop the size of the screen, scaled to fit the tab.
                arguments.Add(Size(display.ScreenWidth, display.ScreenHeight));
                arguments.Add("/smart-sizing");
                break;
            default:
                arguments.Add(Size(display.Width, display.Height));
                if (connection.AutomaticResize)
                    arguments.Add("/dynamic-resolution");
                break;
        }

        return display.ParentWindow is null;
    }

    private static string Size(int width, int height) =>
        string.Create(CultureInfo.InvariantCulture, $"/size:{Math.Max(width, 200)}x{Math.Max(height, 200)}");

    private static void AddSecurity(ConnectionInfo connection, List<string> arguments, List<string> warnings)
    {
        if (!connection.UseCredSsp)
            arguments.Add("/sec:nla:off");
        if (connection.UseConsoleSession)
            arguments.Add("/admin");
        if (connection.UseRestrictedAdmin)
            arguments.Add("/restricted-admin");
        if (connection.UseRCG)
            arguments.Add("/remoteGuard");
        if (connection.UseVmId && connection.VmId.Trim().Length > 0)
        {
            arguments.Add("/vmconnect:" + connection.VmId.Trim());
            if (connection.UseEnhancedMode)
                warnings.Add(UiText.RdpSettingIgnored("enhanced session mode"));
        }
    }

    private static void AddRedirection(ConnectionInfo connection, List<string> arguments, List<string> warnings)
    {
        switch (connection.RedirectDiskDrives)
        {
            case RDPDiskDrives.Local:
                arguments.Add("+home-drive");
                break;
            case RDPDiskDrives.All:
                arguments.Add("+drives");
                break;
            case RDPDiskDrives.Custom:
                // Drive letters mean nothing on Linux; the home folder is the nearest equivalent.
                arguments.Add("+home-drive");
                warnings.Add(UiText.RdpCustomDrivesAsHome);
                break;
        }

        if (connection.RedirectPrinters)
            arguments.Add("/printer");
        if (connection.RedirectSmartCards)
            arguments.Add("/smartcard");
        if (connection.RedirectPorts)
            warnings.Add(UiText.RdpSettingIgnored("serial and parallel port redirection"));
        arguments.Add(connection.RedirectClipboard ? "+clipboard" : "-clipboard");

        switch (connection.RedirectSound)
        {
            case RDPSounds.BringToThisComputer:
                arguments.Add("/sound:quality:" + connection.SoundQuality.ToString().ToLowerInvariant());
                break;
            case RDPSounds.LeaveAtRemoteComputer:
                arguments.Add("/audio-mode:1");
                break;
            default:
                arguments.Add("/audio-mode:2");
                break;
        }

        if (connection.RedirectAudioCapture)
            arguments.Add("/microphone");
    }

    private static void AddExperience(ConnectionInfo connection, List<string> arguments)
    {
        arguments.Add(connection.DisplayWallpaper ? "+wallpaper" : "-wallpaper");
        arguments.Add(connection.DisplayThemes ? "+themes" : "-themes");
        arguments.Add(connection.EnableFontSmoothing ? "+fonts" : "-fonts");
        arguments.Add(connection.EnableDesktopComposition ? "+aero" : "-aero");
        arguments.Add(connection.DisableFullWindowDrag ? "-window-drag" : "+window-drag");
        arguments.Add(connection.DisableMenuAnimations ? "-menu-anims" : "+menu-anims");
        if (connection.CacheBitmaps)
            arguments.Add("/cache:persist");
    }

    private static void AddSession(ConnectionInfo connection, List<string> arguments, List<string> warnings)
    {
        if (connection.LoadBalanceInfo.Trim().Length > 0)
            arguments.Add("/load-balance-info:" + connection.LoadBalanceInfo.Trim());
        if (connection.RDPStartProgram.Trim().Length > 0)
            arguments.Add("/shell:" + connection.RDPStartProgram.Trim());
        if (connection.RDPStartProgramWorkDir.Trim().Length > 0)
            arguments.Add("/shell-dir:" + connection.RDPStartProgramWorkDir.Trim());
        if (connection.RDPMinutesToIdleTimeout > 0)
            warnings.Add(UiText.RdpSettingIgnored("idle timeout"));
        if (connection.ExternalCredentialProvider != ExternalCredentialProvider.None)
            warnings.Add(UiText.ExternalCredentialProviderIgnored);
    }

    private static void AddGateway(ConnectionInfo connection, LoginCredentials credentials, List<string> arguments, List<string> warnings)
    {
        if (connection.RDGatewayUsageMethod == RDGatewayUsageMethod.Never)
            return;

        string host = connection.RDGatewayHostname.Trim();
        if (host.Length == 0)
        {
            warnings.Add(UiText.RdpGatewayWithoutHost);
            return;
        }

        List<string> values = ["g:" + host];
        switch (connection.RDGatewayUseConnectionCredentials)
        {
            case RDGatewayUseConnectionCredentials.Yes:
                AddLogin(values, credentials.Username, credentials.Domain, credentials.Password);
                break;
            case RDGatewayUseConnectionCredentials.No:
                AddLogin(values, connection.RDGatewayUsername.Trim(), connection.RDGatewayDomain.Trim(), connection.RDGatewayPassword);
                break;
            case RDGatewayUseConnectionCredentials.AccessToken:
                values.Add("access-token:" + connection.RDGatewayAccessToken.Trim());
                break;
            default:
                warnings.Add(UiText.RdpGatewayLoginIgnored);
                break;
        }

        values.Add("usage-method:" + (connection.RDGatewayUsageMethod == RDGatewayUsageMethod.Detect ? "detect" : "direct"));
        // xfreerdp separates the gateway values with commas.
        if (values.Any(value => value.Contains(',') || value.Contains('"')))
            throw new SessionException(UiText.RdpGatewayValueWithComma);
        arguments.Add("/gateway:" + string.Join(',', values));
    }

    private static void AddLogin(List<string> values, string username, string domain, string password)
    {
        if (username.Length > 0)
            values.Add("u:" + username);
        if (domain.Length > 0)
            values.Add("d:" + domain);
        if (password.Length > 0)
            values.Add("p:" + password);
    }

    /// <summary>An argument as shown to the user: passwords and access tokens are replaced.</summary>
    internal static string Redact(string argument)
    {
        if (argument.StartsWith("/p:", StringComparison.Ordinal))
            return "/p:***";
        if (!argument.StartsWith("/gateway:", StringComparison.Ordinal))
            return argument;
        IEnumerable<string> values = argument["/gateway:".Length..].Split(',')
            .Select(value => value.StartsWith("p:", StringComparison.Ordinal) ? "p:***"
                : value.StartsWith("access-token:", StringComparison.Ordinal) ? "access-token:***" : value);
        return "/gateway:" + string.Join(',', values);
    }

    private static string ArgumentName(string argument)
    {
        int colon = argument.IndexOf(':');
        return colon > 0 ? argument[..colon] : argument;
    }
}
