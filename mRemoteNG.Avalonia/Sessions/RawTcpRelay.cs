using System.Globalization;
using System.Net.Sockets;
using Microsoft.Win32.SafeHandles;

namespace mRemoteNG.Avalonia.Sessions;

/// <summary>
/// What a Raw session runs in its terminal: this app with <see cref="Argument"/>. Bytes from the TCP connection are
/// shown as they arrive; lines typed in the terminal are sent with CR LF. The pseudo-terminal echoes and edits each
/// line locally until Enter, which is how PuTTY treats Raw connections.
/// </summary>
internal static class RawTcpRelay
{
    public const string Argument = "--raw-tcp";

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    public static bool IsRequested(string[] args) => args.Length == 3 && args[0] == Argument;

    /// <summary>Relays until either side closes; 0 when the connection closed normally.</summary>
    public static int Run(string[] args)
    {
        string host = args[1];
        if (!CommandLineValue.IsSafe(host) || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int port)
            || port is < 1 or > 65535)
        {
            return 2;
        }

        // The terminal's own file descriptors, not Console streams: those would replace the terminal's line editing.
        using FileStream input = new(new SafeFileHandle(0, ownsHandle: false), FileAccess.Read, bufferSize: 0);
        using FileStream output = new(new SafeFileHandle(1, ownsHandle: false), FileAccess.Write, bufferSize: 0);
        try
        {
            using TcpClient client = new();
            using (CancellationTokenSource timeout = new(ConnectTimeout))
                client.ConnectAsync(host, port, timeout.Token).AsTask().GetAwaiter().GetResult();
            RelayAsync(client.Client, input, output).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            WriteLine(output, UiText.CouldNotConnectTo(host, port, ex.Message));
            return 1;
        }
    }

    /// <summary>
    /// Copies the connection to <paramref name="output"/> and the lines of <paramref name="input"/> to the connection.
    /// Ends when the remote side closes; the end of the input (Ctrl+D) only closes the sending direction.
    /// </summary>
    internal static async Task RelayAsync(Socket socket, Stream input, Stream output)
    {
        await using NetworkStream network = new(socket, ownsSocket: false);
        Task download = network.CopyToAsync(output);
        _ = UploadAsync(input, network, socket);
        await download;
    }

    private static async Task UploadAsync(Stream input, Stream network, Socket socket)
    {
        byte[] buffer = new byte[4096];
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                foreach (ReadOnlyMemory<byte> part in WithCrLf(buffer.AsMemory(0, read)))
                    await network.WriteAsync(part);
            }

            socket.Shutdown(SocketShutdown.Send);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // The connection is gone; the download side ends the relay.
        }
    }

    private static readonly byte[] CrLf = "\r\n"u8.ToArray();

    // The terminal ends a line with LF; network protocols expect CR LF.
    private static IEnumerable<ReadOnlyMemory<byte>> WithCrLf(ReadOnlyMemory<byte> data)
    {
        int start = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if (data.Span[i] != (byte)'\n' || (i > 0 && data.Span[i - 1] == (byte)'\r'))
                continue;
            yield return data[start..i];
            yield return CrLf;
            start = i + 1;
        }

        if (start < data.Length)
            yield return data[start..];
    }

    private static void WriteLine(Stream output, string text)
    {
        try
        {
            output.Write(System.Text.Encoding.UTF8.GetBytes(text + "\n"));
        }
        catch (IOException)
        {
        }
    }
}
