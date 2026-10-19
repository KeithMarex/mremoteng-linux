using System.Runtime.InteropServices;

namespace mRemoteNG.Avalonia.Sessions;

/// <summary>The pseudo-terminal a relay runs in: its size, and raw mode for protocols where the server echoes.</summary>
internal static class TerminalDevice
{
    private const int StandardInput = 0;
    private const int StandardOutput = 1;
    private const int SetNow = 0; // TCSANOW
    private const nuint GetWindowSize = 0x5413; // TIOCGWINSZ (Linux, x86-64 and arm64)

    // Larger than any libc's struct termios (60 bytes on glibc); only libc reads and writes it.
    private const int TermiosSize = 256;

    /// <summary>The terminal's size; 80×24 when it cannot be read.</summary>
    public static WindowSize Size()
    {
        WindowSize size = default;
        return ioctl(StandardOutput, GetWindowSize, ref size) == 0 && size.Columns > 0 && size.Rows > 0
            ? size
            : new WindowSize { Rows = 24, Columns = 80 };
    }

    /// <summary>
    /// Switches the terminal to raw mode: no local echo or line editing, and Ctrl+C reaches the server as a byte.
    /// Disposing the result restores the previous mode; null when standard input is not a terminal.
    /// </summary>
    public static IDisposable? EnterRawMode()
    {
        byte[] original = new byte[TermiosSize];
        if (tcgetattr(StandardInput, original) != 0)
            return null;

        byte[] raw = (byte[])original.Clone();
        cfmakeraw(raw);
        return tcsetattr(StandardInput, SetNow, raw) == 0 ? new Restore(original) : null;
    }

    private sealed class Restore(byte[] original) : IDisposable
    {
        public void Dispose() => tcsetattr(StandardInput, SetNow, original);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int tcgetattr(int fd, [Out] byte[] termios);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcsetattr(int fd, int optionalActions, [In] byte[] termios);

    [DllImport("libc")]
    private static extern void cfmakeraw([In, Out] byte[] termios);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, nuint request, ref WindowSize size);
}

/// <summary>struct winsize.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WindowSize
{
    public ushort Rows;
    public ushort Columns;
    public ushort Width;
    public ushort Height;
}
