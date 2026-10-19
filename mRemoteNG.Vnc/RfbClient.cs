using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using mRemoteNG.Vnc.Encodings;

namespace mRemoteNG.Vnc;

/// <summary>The remote mouse pointer's shape: BGRA pixels with real alpha, and its hot spot.</summary>
public sealed record CursorShape(int Width, int Height, int HotX, int HotY, uint[] Pixels);

/// <summary>
/// A VNC (RFB 3.3–3.8) session: logs in, keeps <see cref="Framebuffer"/> up to date and sends keys, pointer and
/// clipboard. Events are raised on the session's reader thread.
/// </summary>
public sealed class RfbClient : IDisposable
{
    private const int DesktopSizeEncoding = -223;
    private const int LastRectEncoding = -224;
    private const int CursorEncoding = -239;
    private const int MaxClipboard = 10 * 1024 * 1024;

    private readonly Stream _connection;
    private readonly RfbStream _input;
    private readonly Lock _sending = new();
    private readonly IRfbDecoder[] _decodersByPreference;
    private readonly Dictionary<int, IRfbDecoder> _decoders;
    private Thread? _reader;
    private volatile bool _closed;

    private RfbClient(Stream connection, RfbStream input, RfbHandshake.ServerInit init)
    {
        _connection = connection;
        _input = input;
        DesktopName = init.Name;
        Framebuffer = new Framebuffer(init.Width, init.Height);
        _decodersByPreference = [new CopyRectDecoder(), new ZrleDecoder(), new HextileDecoder(), new RreDecoder(), new RawDecoder()];
        _decoders = _decodersByPreference.ToDictionary(decoder => decoder.Encoding);
    }

    public string DesktopName { get; }

    /// <summary>The remote screen; replaced by a new one when the server's screen size changes.</summary>
    public Framebuffer Framebuffer { get; private set; }

    /// <summary>Part of the screen changed.</summary>
    public event Action<ScreenRect>? Updated;

    /// <summary>The screen size changed: <see cref="Framebuffer"/> is a new one.</summary>
    public event Action? Resized;

    public event Action<CursorShape>? CursorChanged;

    /// <summary>The remote clipboard holds new text.</summary>
    public event Action<string>? ClipboardChanged;

    public event Action? Bell;

    /// <summary>The session ended: null when it was closed here, else why.</summary>
    public event Action<Exception?>? Closed;

    /// <summary>Connects and logs in; the session starts with <see cref="Start"/>.</summary>
    /// <exception cref="RfbException">The server refused or broke the protocol.</exception>
    /// <exception cref="RfbAuthenticationException">The login was refused.</exception>
    /// <exception cref="OperationCanceledException">Cancelled, or asking for the login was.</exception>
    public static async Task<RfbClient> ConnectAsync(string host, int port, RfbLogin login, CancellationToken cancellation)
    {
        TcpClient tcp = new() { NoDelay = true };
        try
        {
            await tcp.ConnectAsync(host, port, cancellation);
            return await HandshakeAsync(tcp.GetStream(), login, cancellation);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    /// <summary>Logs in on an open connection (the client owns it from now on).</summary>
    public static Task<RfbClient> HandshakeAsync(Stream connection, RfbLogin login, CancellationToken cancellation) => Task.Run(() =>
    {
        // Reading blocks; cancelling closes the connection, which ends the read.
        using CancellationTokenRegistration abort = cancellation.Register(connection.Dispose);
        try
        {
            RfbStream input = new(connection);
            RfbHandshake.ServerInit init = RfbHandshake.Run(input, connection, login);
            RfbClient client = new(connection, input, init);
            client.SendSetup();
            return client;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            connection.Dispose();
            cancellation.ThrowIfCancellationRequested();
            throw new RfbException(ex.Message);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }, cancellation);

    /// <summary>Starts reading the server's updates, once the events are subscribed.</summary>
    public void Start()
    {
        _reader = new Thread(Read) { IsBackground = true, Name = "VNC session" };
        _reader.Start();
    }

    /// <summary>Presses or releases a key, given as X11 keysym.</summary>
    public void SendKey(uint keysym, bool down)
    {
        byte[] message = new byte[8];
        message[0] = 4;
        message[1] = (byte)(down ? 1 : 0);
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(4), keysym);
        Send(message);
    }

    /// <summary>Moves the pointer; <paramref name="buttons"/> has bit 0 for the left button, 1 middle, 2 right, 3–6 the wheel.</summary>
    public void SendPointer(int x, int y, byte buttons)
    {
        byte[] message = new byte[6];
        message[0] = 5;
        message[1] = buttons;
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(2), (ushort)Math.Clamp(x, 0, ushort.MaxValue));
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(4), (ushort)Math.Clamp(y, 0, ushort.MaxValue));
        Send(message);
    }

    /// <summary>Puts text on the remote clipboard (Latin-1, as RFB has it).</summary>
    public void SendClipboard(string text)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(text.Replace("\r\n", "\n"));
        byte[] message = new byte[8 + bytes.Length];
        message[0] = 6;
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(4), (uint)bytes.Length);
        bytes.CopyTo(message, 8);
        Send(message);
    }

    /// <summary>Asks for the whole screen again.</summary>
    public void Refresh() => RequestUpdate(incremental: false);

    public void Dispose()
    {
        _closed = true;
        _connection.Dispose();
        foreach (IDisposable decoder in _decodersByPreference.OfType<IDisposable>())
            decoder.Dispose();
    }

    // Our pixel format (32-bit true colour, little-endian, BGRA in memory), the encodings, and a first full update.
    private void SendSetup()
    {
        byte[] pixelFormat = [0, 0, 0, 0, 32, 24, 0, 1, 0, 255, 0, 255, 0, 255, 16, 8, 0, 0, 0, 0];
        Send(pixelFormat);

        int[] encodings = [.. _decodersByPreference.Select(decoder => decoder.Encoding), CursorEncoding, DesktopSizeEncoding, LastRectEncoding];
        byte[] message = new byte[4 + 4 * encodings.Length];
        message[0] = 2;
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(2), (ushort)encodings.Length);
        for (int i = 0; i < encodings.Length; i++)
            BinaryPrimitives.WriteInt32BigEndian(message.AsSpan(4 + 4 * i), encodings[i]);
        Send(message);
        RequestUpdate(incremental: false);
    }

    private void RequestUpdate(bool incremental)
    {
        Framebuffer framebuffer = Framebuffer;
        byte[] message = new byte[10];
        message[0] = 3;
        message[1] = (byte)(incremental ? 1 : 0);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(6), (ushort)framebuffer.Width);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(8), (ushort)framebuffer.Height);
        Send(message);
    }

    private void Send(byte[] message)
    {
        if (_closed)
            return;
        lock (_sending)
        {
            try
            {
                _connection.Write(message);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
            {
                // The reader notices the closed connection and reports it.
            }
        }
    }

    private void Read()
    {
        Exception? error = null;
        try
        {
            while (!_closed)
            {
                byte type = _input.ReadByte();
                switch (type)
                {
                    case 0:
                        ReadUpdate();
                        break;
                    case 1:
                        // Colour map entries; this client uses true colour.
                        _input.Skip(3);
                        _input.Skip(_input.ReadUInt16() * 6);
                        break;
                    case 2:
                        Bell?.Invoke();
                        break;
                    case 3:
                        _input.Skip(3);
                        ClipboardChanged?.Invoke(_input.ReadString(MaxClipboard, Encoding.Latin1));
                        break;
                    default:
                        throw new RfbException($"The server sent an unknown message ({type}).");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or RfbException)
        {
            error = _closed ? null : ex is RfbException ? ex : new RfbException(ex.Message);
        }

        _closed = true;
        Closed?.Invoke(error);
    }

    private void ReadUpdate()
    {
        _input.Skip(1);
        int count = _input.ReadUInt16();
        ScreenRect changed = default;
        for (int i = 0; i < count; i++)
        {
            ScreenRect rect = new(_input.ReadUInt16(), _input.ReadUInt16(), _input.ReadUInt16(), _input.ReadUInt16());
            int encoding = _input.ReadInt32();
            if (encoding == LastRectEncoding)
                break;
            if (encoding == DesktopSizeEncoding)
            {
                Framebuffer = new Framebuffer(rect.Width, rect.Height);
                changed = default;
                Resized?.Invoke();
                continue;
            }

            if (encoding == CursorEncoding)
            {
                ReadCursor(rect);
                continue;
            }

            if (!_decoders.TryGetValue(encoding, out IRfbDecoder? decoder))
                throw new RfbException($"The server sent an encoding this client did not ask for ({encoding}).");
            Framebuffer.Check(rect);
            decoder.Decode(_input, Framebuffer, rect);
            changed = changed.Union(rect);
        }

        if (!changed.IsEmpty)
            Updated?.Invoke(changed);
        RequestUpdate(incremental: true);
    }

    // The pointer's pixels, then a bit mask of which are part of it; the rectangle's position is the hot spot.
    private void ReadCursor(ScreenRect rect)
    {
        if (rect.Width > 256 || rect.Height > 256)
            throw new RfbException("The server sent an oversized pointer.");
        uint[] pixels = new uint[rect.Width * rect.Height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = _input.ReadPixel();
        int maskRow = (rect.Width + 7) / 8;
        byte[] mask = _input.ReadBytes(maskRow * rect.Height);
        for (int y = 0; y < rect.Height; y++)
        {
            for (int x = 0; x < rect.Width; x++)
            {
                bool visible = (mask[y * maskRow + x / 8] & (0x80 >> (x % 8))) != 0;
                int i = y * rect.Width + x;
                pixels[i] = visible ? (pixels[i] & 0x00FFFFFF) | 0xFF000000 : 0;
            }
        }

        if (rect.Width > 0 && rect.Height > 0)
            CursorChanged?.Invoke(new CursorShape(rect.Width, rect.Height, rect.X, rect.Y, pixels));
    }
}
