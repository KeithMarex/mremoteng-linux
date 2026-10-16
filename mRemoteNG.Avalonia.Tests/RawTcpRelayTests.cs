using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using mRemoteNG.Avalonia.Sessions;
using NUnit.Framework;

namespace mRemoteNG.Avalonia.Tests;

public class RawTcpRelayTests
{
    [Test]
    public async Task TypedLinesAreSentWithCrLfAndTheRepliesShown()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        Task<string> server = ServeAsync(listener, reply: "pong");

        using Socket client = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        MemoryStream output = new();
        await RawTcpRelay.RelayAsync(client, new MemoryStream("ping\nsecond\r\n"u8.ToArray()), output);

        Assert.That(await server, Is.EqualTo("ping\r\nsecond\r\n"));
        Assert.That(Encoding.UTF8.GetString(output.ToArray()), Is.EqualTo("pong"));
    }

    [Test]
    public async Task TheAppsExecutableRelaysWhenARawSessionStartsIt()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        Task<string> server = ServeAsync(listener, reply: "pong");
        string app = Path.Combine(AppContext.BaseDirectory, "mRemoteNG.Avalonia");
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        ProcessStartInfo start = new(app, [RawTcpRelay.Argument, "127.0.0.1", port.ToString()])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        using Process process = Process.Start(start)!;
        await process.StandardInput.WriteAsync("ping\n");
        process.StandardInput.Close();
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync(new CancellationTokenSource(30_000).Token);

        Assert.That(await server, Is.EqualTo("ping\r\n"));
        Assert.That((process.ExitCode, output), Is.EqualTo((0, "pong")));
    }

    [Test]
    public void AnUnsafeHostIsRefused()
    {
        Assert.That(RawTcpRelay.Run([RawTcpRelay.Argument, "-oops", "23"]), Is.EqualTo(2));
    }

    // Reads until the client stops sending, then answers and closes.
    private static async Task<string> ServeAsync(TcpListener listener, string reply)
    {
        using Socket connection = await listener.AcceptSocketAsync();
        await using NetworkStream stream = new(connection);
        MemoryStream received = new();
        await stream.CopyToAsync(received);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(reply));
        connection.Shutdown(SocketShutdown.Both);
        return Encoding.UTF8.GetString(received.ToArray());
    }
}
