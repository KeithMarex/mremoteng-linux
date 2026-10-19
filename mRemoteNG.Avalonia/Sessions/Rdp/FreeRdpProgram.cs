using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace mRemoteNG.Avalonia.Sessions.Rdp;

/// <summary>Finds FreeRDP 3's X11 client, and explains what its exit codes mean.</summary>
internal static partial class FreeRdpProgram
{
    /// <summary>
    /// xfreerdp3 (Debian and Ubuntu), or else an xfreerdp of version 3 or later (some distributions drop the "3").
    /// FreeRDP 2 lacks /args-from, which keeps the password off the command line.
    /// </summary>
    /// <param name="majorVersion">Reads a program's FreeRDP major version; null when it cannot tell.</param>
    /// <exception cref="SessionException">No FreeRDP 3 client is installed.</exception>
    public static string Locate(SessionPrograms programs, Func<string, int?>? majorVersion = null)
    {
        if (programs.Find(programs.FreeRdp) is { } freeRdp3)
            return freeRdp3;
        if (programs.Find("xfreerdp") is { } freeRdp && (majorVersion ?? MajorVersion)(freeRdp) >= 3)
            return freeRdp;
        throw new SessionException(UiText.FreeRdpNotFound);
    }

    private static int? MajorVersion(string program)
    {
        try
        {
            ProcessStartInfo info = new(program, ["/version"]) { RedirectStandardOutput = true, UseShellExecute = false };
            using Process process = Process.Start(info)!;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            Match match = VersionPattern().Match(output);
            return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"FreeRDP version (\d+)\.")]
    private static partial Regex VersionPattern();

    /// <summary>Whether xfreerdp ended because the session was over (logged off, or closed on purpose), not because of a problem.</summary>
    public static bool IsNormalEnd(int exitCode) => exitCode is Success or LogOff or DisconnectedByUser;

    private const int Success = 0;
    private const int LogOff = 2;
    private const int DisconnectedByUser = 11;

    /// <summary>Why xfreerdp ended, for the user; null when its exit code says nothing more than the number.</summary>
    public static string? Reason(int exitCode) => exitCode switch
    {
        Success => null,
        1 => "The session was disconnected by the server or another user.",
        LogOff => "You logged off.",
        3 => "The session was idle for too long.",
        4 => "The logon took too long.",
        5 => "Another connection to this session took it over.",
        7 => "The server refused the connection.",
        9 => "This user may not connect to the server.",
        10 => "The server asks for the password again.",
        DisconnectedByUser => "The session was disconnected from the remote computer.",
        >= 16 and <= 26 => "The server's Remote Desktop licensing refused the connection.",
        >= 32 and <= 127 => "The connection failed with a Remote Desktop protocol error.",
        128 => "xfreerdp did not accept its options.",
        131 or 137 or 141 or 147 => "Could not connect to the computer.",
        132 or 134 or 154 => "The logon failed: check the user name, domain and password.",
        133 => "The security negotiation failed; try changing \"Use CredSSP\" or the authentication level.",
        135 => "The account is locked out.",
        139 or 140 => "The computer's name could not be found.",
        143 => "The secure (TLS) connection failed, or its certificate was not accepted.",
        144 or 155 or 158 => "Access was denied.",
        145 => "Connecting was cancelled.",
        148 or 152 => "The password has expired.",
        149 => "The password must be changed before logging on.",
        150 => "The Kerberos domain controller cannot be reached.",
        151 => "The account is disabled.",
        156 => "An account restriction prevents this logon.",
        157 => "The account has expired.",
        _ => null
    };
}
