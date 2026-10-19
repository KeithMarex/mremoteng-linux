using System.Buffers.Binary;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace mRemoteNG.Avalonia.Sessions;

/// <summary>
/// What an Rlogin session runs in its terminal when no rlogin client is installed: this app with
/// <see cref="Argument"/>, speaking rlogin (RFC 1282) as PuTTY does on Windows. It logs in as the local user, like
/// PuTTY without a configured user name. The terminal is in raw mode, because the server echoes and edits lines.
/// Unlike the setuid rlogin client it cannot use a privileged port, which most rlogin servers require.
/// </summary>
internal static class RloginRelay
{
    public const string Argument = "--rlogin";

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    public static bool IsRequested(string[] args) => args.Length == 3 && args[0] == Argument;

    /// <summary>
    /// Relays until the server closes the connection: 0 after a normal logout, 1 when the server refused the login or the
    /// connection failed (the terminal then shows why, and the tab stays open).
    /// </summary>
    public static int Run(string[] args)
    {
        string host = args[1];
        if (!CommandLineValue.IsSafe(host) || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            || port is < 1 or > 65535)
        {
            return 2;
        }

        using FileStream input = new(new SafeFileHandle(0, ownsHandle: false), FileAccess.Read, bufferSize: 0);
        using FileStream output = new(new SafeFileHandle(1, ownsHandle: false), FileAccess.Write, bufferSize: 0);
        string user = Environment.UserName;
        string terminal = Environment.GetEnvironmentVariable("TERM") is { Length: > 0 } term ? term : "xterm";
        try
        {
            using TcpClient client = new();
            using (CancellationTokenSource timeout = new(ConnectTimeout))
                client.ConnectAsync(host, port, timeout.Token).AsTask().GetAwaiter().GetResult();

            using IDisposable? rawMode = TerminalDevice.EnterRawMode();
            RloginSession session = new(client.Client, input, output, TerminalDevice.Size);
            using PosixSignalRegistration? resized = OperatingSystem.IsWindows()
                ? null
                : PosixSignalRegistration.Create(PosixSignal.SIGWINCH, _ => session.WindowChanged());
            return session.RunAsync(user, user, terminal).GetAwaiter().GetResult() ? 0 : 1;
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            try
            {
                output.Write(Encoding.UTF8.GetBytes(UiText.CouldNotConnectTo(host, port, ex.Message) + "\r\n"));
            }
            catch (IOException)
            {
            }

            return 1;
        }
    }
}

/// <summary>The rlogin protocol on a connected socket, with the terminal as <paramref name="input"/> and <paramref name="output"/>.</summary>
/// <param name="windowSize">Reads the terminal's size.</param>
internal sealed class RloginSession(Socket socket, Stream input, Stream output, Func<WindowSize> windowSize)
{
    // Urgent-data flag (TIOCPKT_WINDOW) with which the server asks for the window size, now and after every change.
    private const byte WindowSizeRequest = 0x80;

    private readonly Lock _sending = new();
    private volatile bool _windowSizeWanted;

    /// <summary>
    /// Logs in and relays until the server closes the connection; false when it refused the login or the connection
    /// was lost, after writing why to the terminal.
    /// </summary>
    public async Task<bool> RunAsync(string localUser, string remoteUser, string terminal)
    {
        Send(Encoding.UTF8.GetBytes($"\0{localUser}\0{remoteUser}\0{terminal}/38400\0"));
        Task<bool> download = Task.Factory.StartNew(Download, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        _ = UploadAsync();
        return await download;
    }

    /// <summary>The terminal was resized: tells the server, once it has asked for the window size.</summary>
    public void WindowChanged()
    {
        if (_windowSizeWanted)
            SendWindowSize();
    }

    private bool Download()
    {
        byte[] buffer = new byte[8192];
        bool first = true;
        bool refused = false;
        List<Socket> readable = new(1);
        List<Socket> urgent = new(1);
        try
        {
            while (true)
            {
                readable.Clear();
                readable.Add(socket);
                urgent.Clear();
                urgent.Add(socket);
                Socket.Select(readable, null, urgent, -1);
                if (urgent.Count > 0)
                    ReadUrgentData();
                if (readable.Count == 0)
                    continue;

                // Stops at the urgent mark, so the flag is read in order with the data.
                int count = socket.Receive(buffer);
                if (count == 0)
                {
                    // Servers hang up at once on clients that are not on a privileged port.
                    if (first)
                        WriteLine($"{UiText.ServerClosedConnection} {UiText.NoRloginClient}");
                    return !first && !refused;
                }

                ReadOnlySpan<byte> data = buffer.AsSpan(0, count);
                // The server first sends a zero byte, or byte 1 and the reason it refuses.
                if (first)
                {
                    first = false;
                    refused = data[0] == 1;
                    if (data[0] is 0 or 1)
                        data = data[1..];
                }

                output.Write(data);
            }
        }
        catch (SocketException ex)
        {
            WriteLine(UiText.ConnectionLost(ex.Message));
            return false;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The terminal is gone.
            return true;
        }
    }

    private void WriteLine(string text)
    {
        try
        {
            output.Write(Encoding.UTF8.GetBytes($"\r\n{text}\r\n"));
        }
        catch (IOException)
        {
        }
    }

    private void ReadUrgentData()
    {
        byte[] flag = new byte[1];
        try
        {
            if (socket.Receive(flag, SocketFlags.OutOfBand) != 1 || (flag[0] & WindowSizeRequest) == 0)
                return;
        }
        catch (SocketException)
        {
            // The urgent byte was already read.
            return;
        }

        _windowSizeWanted = true;
        SendWindowSize();
    }

    private void SendWindowSize()
    {
        WindowSize size = windowSize();
        byte[] message = new byte[12];
        message[0] = message[1] = 0xFF;
        message[2] = message[3] = (byte)'s';
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(4), size.Rows);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(6), size.Columns);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(8), size.Width);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(10), size.Height);
        Send(message);
    }

    private async Task UploadAsync()
    {
        byte[] buffer = new byte[4096];
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
                Send(buffer.AsSpan(0, read));
            socket.Shutdown(SocketShutdown.Send);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // The connection is gone; the download side ends the relay.
        }
    }

    // Typed input and window-size messages come from different threads; neither may split the other.
    private void Send(ReadOnlySpan<byte> data)
    {
        lock (_sending)
        {
            try
            {
                while (data.Length > 0)
                    data = data[socket.Send(data)..];
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                // The download side notices the closed connection.
            }
        }
    }
}
