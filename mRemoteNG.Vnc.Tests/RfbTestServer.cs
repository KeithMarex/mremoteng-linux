using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace mRemoteNG.Vnc.Tests;

/// <summary>The server side of a test session: a loopback connection the test scripts byte by byte.</summary>
internal sealed class RfbTestServer : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly TcpListener _listener;
    private readonly MemoryStream _zlibOutput = new();
    private ZLibStream? _zlib;
    private Socket? _socket;
    private NetworkStream? _stream;

    public RfbTestServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public NetworkStream Stream => _stream ?? throw new InvalidOperationException("Not accepted yet");

    public async Task AcceptAsync()
    {
        _socket = await _listener.AcceptSocketAsync().WaitAsync(Timeout);
        _stream = new NetworkStream(_socket, ownsSocket: true) { ReadTimeout = (int)Timeout.TotalMilliseconds };
    }

    /// <summary>Version 3.8 and the given security types, returning the client's choice.</summary>
    public async Task<byte> OfferAsync(params byte[] types)
    {
        await AcceptAsync();
        Write("RFB 003.008\n"u8);
        Assert(Encoding.ASCII.GetString(Read(12)) == "RFB 003.008\n", "client version");
        Write([(byte)types.Length, .. types]);
        return Read(1)[0];
    }

    /// <summary>SecurityResult OK, then ClientInit, ServerInit and the client's setup messages.</summary>
    public void Accept(int width, int height, string name = "test desktop")
    {
        WriteUInt32(0);
        Init(width, height, name);
    }

    public void Init(int width, int height, string name = "test desktop")
    {
        Assert(Read(1)[0] == 1, "shared flag");
        byte[] init = new byte[20]; // Width, height, then the server's pixel format.
        BinaryPrimitives.WriteUInt16BigEndian(init, (ushort)width);
        BinaryPrimitives.WriteUInt16BigEndian(init.AsSpan(2), (ushort)height);
        Write(init);
        WriteString(name);
        SetPixelFormat = Read(20);
        byte[] header = Read(4);
        Encodings = Enumerable.Range(0, BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2)))
            .Select(_ => BinaryPrimitives.ReadInt32BigEndian(Read(4))).ToArray();
        FirstUpdateRequest = Read(10);
    }

    public byte[] SetPixelFormat { get; private set; } = [];

    public int[] Encodings { get; private set; } = [];

    public byte[] FirstUpdateRequest { get; private set; } = [];

    /// <summary>A FramebufferUpdate header for <paramref name="count"/> rectangles.</summary>
    public void BeginUpdate(int count) => Write([0, 0, (byte)(count >> 8), (byte)count]);

    public void Rect(int x, int y, int width, int height, int encoding)
    {
        byte[] header = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header, (ushort)x);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), (ushort)y);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)width);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), (ushort)height);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(8), encoding);
        Write(header);
    }

    /// <summary>A pixel in the client's format (little-endian B, G, R, padding).</summary>
    public void Pixel(uint pixel)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, pixel);
        Write(bytes);
    }

    /// <summary>ZRLE data for one rectangle, compressed on the session's one zlib stream.</summary>
    public void Zrle(byte[] tiles)
    {
        _zlib ??= new ZLibStream(_zlibOutput, CompressionLevel.Optimal, leaveOpen: true);
        _zlibOutput.SetLength(0);
        _zlib.Write(tiles);
        _zlib.Flush();
        WriteUInt32((uint)_zlibOutput.Length);
        Write(_zlibOutput.ToArray());
    }

    public void Write(ReadOnlySpan<byte> bytes) => Stream.Write(bytes);

    public void WriteUInt32(uint value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        Write(bytes);
    }

    public void WriteString(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        WriteUInt32((uint)bytes.Length);
        Write(bytes);
    }

    public byte[] Read(int count)
    {
        byte[] buffer = new byte[count];
        Stream.ReadExactly(buffer);
        return buffer;
    }

    public void Dispose()
    {
        _zlib?.Dispose();
        _stream?.Dispose();
        _listener.Stop();
        _listener.Dispose();
    }

    private static void Assert(bool condition, string what)
    {
        if (!condition)
            throw new InvalidOperationException("Unexpected " + what);
    }
}
