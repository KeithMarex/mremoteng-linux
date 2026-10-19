using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using mRemoteNG.Avalonia.Sessions;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class RloginRelayTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Test]
    public async Task LogsInSendsTheWindowSizeWhenAskedAndRelays()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        using Socket client = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using Socket server = await listener.AcceptSocketAsync();

        using AnonymousPipeServerStream typing = new(PipeDirection.Out);
        using AnonymousPipeClientStream input = new(PipeDirection.In, typing.ClientSafePipeHandle);
        MemoryStream output = new();
        WindowSize size = new() { Rows = 40, Columns = 120 };
        RloginSession session = new(client, input, output, () => size);
        Task<bool> run = session.RunAsync("koen", "koen", "xterm-256color");

        byte[] login = "\0koen\0koen\0xterm-256color/38400\0"u8.ToArray();
        Assert.That(await ReceiveAsync(server, login.Length), Is.EqualTo(login));

        session.WindowChanged(); // Not asked for yet: nothing is sent.
        server.Send("\0Welcome\r\n"u8);
        server.Send([0x80], SocketFlags.OutOfBand);
        Assert.That(await ReceiveAsync(server, 12), Is.EqualTo(WindowSizeMessage(40, 120)));

        size = new WindowSize { Rows = 50, Columns = 132 };
        session.WindowChanged();
        Assert.That(await ReceiveAsync(server, 12), Is.EqualTo(WindowSizeMessage(50, 132)));

        typing.Write("ls\x03"u8);
        Assert.That(await ReceiveAsync(server, 3), Is.EqualTo("ls\x03"u8.ToArray()));

        server.Send("bye"u8);
        server.Shutdown(SocketShutdown.Both);
        Assert.That(await run.WaitAsync(Timeout), Is.True);
        Assert.That(Encoding.UTF8.GetString(output.ToArray()), Is.EqualTo("Welcome\r\nbye"));
    }

    [Test]
    public async Task TheServersReasonForRefusingIsShown()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        using Socket client = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using Socket server = await listener.AcceptSocketAsync();
        MemoryStream output = new();
        RloginSession session = new(client, new MemoryStream(), output, () => default);

        Task<bool> run = session.RunAsync("koen", "koen", "xterm");
        await ReceiveAsync(server, "\0koen\0koen\0xterm/38400\0".Length);
        server.Send("\u0001Permission denied.\r\n"u8);
        server.Shutdown(SocketShutdown.Both);

        Assert.That(await run.WaitAsync(Timeout), Is.False);
        Assert.That(Encoding.UTF8.GetString(output.ToArray()), Is.EqualTo("Permission denied.\r\n"));
    }

    [Test]
    public async Task AServerThatHangsUpAtOnceIsReported()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        using Socket client = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using Socket server = await listener.AcceptSocketAsync();
        MemoryStream output = new();
        RloginSession session = new(client, new MemoryStream(), output, () => default);

        Task<bool> run = session.RunAsync("koen", "koen", "xterm");
        // Closing with unread data resets the connection, as netkit's rlogind does to clients on unprivileged ports.
        await Task.Delay(100);
        server.Close(0);

        Assert.That(await run.WaitAsync(Timeout), Is.False);
        Assert.That(Encoding.UTF8.GetString(output.ToArray()), Does.Contain("connection"));
    }

    [Test]
    public async Task TheAppsExecutableRelaysWhenAnRloginSessionStartsIt()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        string app = Path.Combine(AppContext.BaseDirectory, "mRemoteNG.Avalonia");
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        ProcessStartInfo start = new(app, [RloginRelay.Argument, "127.0.0.1", port.ToString()])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            Environment = { ["TERM"] = "vt100" }
        };

        using Process process = Process.Start(start)!;
        using Socket server = await listener.AcceptSocketAsync();
        string login = Encoding.UTF8.GetString(await ReceiveAsync(server, 3 + 2 * Environment.UserName.Length + "vt100/38400\0".Length));
        server.Send("\0hello"u8);
        server.Shutdown(SocketShutdown.Both);
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync(new CancellationTokenSource(Timeout).Token);

        Assert.That(login, Is.EqualTo($"\0{Environment.UserName}\0{Environment.UserName}\0vt100/38400\0"));
        Assert.That((process.ExitCode, output), Is.EqualTo((0, "hello")));
    }

    [Test]
    public void AnUnsafeHostIsRefused()
    {
        Assert.That(RloginRelay.Run([RloginRelay.Argument, "-oops", "513"]), Is.EqualTo(2));
    }

    private static byte[] WindowSizeMessage(ushort rows, ushort columns)
    {
        byte[] message = [0xFF, 0xFF, (byte)'s', (byte)'s', 0, 0, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(4), rows);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(6), columns);
        return message;
    }

    private static async Task<byte[]> ReceiveAsync(Socket socket, int count)
    {
        byte[] buffer = new byte[count];
        using CancellationTokenSource timeout = new(Timeout);
        int received = 0;
        while (received < count)
        {
            int read = await socket.ReceiveAsync(buffer.AsMemory(received), SocketFlags.None, timeout.Token);
            if (read == 0)
                break;
            received += read;
        }

        return buffer[..received];
    }
}
