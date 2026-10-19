using System.IO.Compression;

namespace mRemoteNG.Vnc.Encodings;

/// <summary>
/// ZRLE (encoding 16): zlib-compressed 64×64 tiles, each raw, solid, palette-packed or run-length encoded. One zlib
/// stream runs through the whole connection, so this decoder lives as long as the session.
/// </summary>
internal sealed class ZrleDecoder : IRfbDecoder, IDisposable
{
    private const int TileSide = 64;

    // Against a server that announces more data than a screen can need.
    private const uint MaxCompressedLength = 64 * 1024 * 1024;

    private readonly FeedStream _compressed = new();
    private readonly ZLibStream _zlib;
    private readonly byte[] _scratch = new byte[TileSide * TileSide * 3];
    private readonly uint[] _palette = new uint[128];
    private readonly uint[] _tile = new uint[TileSide * TileSide];
    private int _paletteSize;

    public ZrleDecoder()
    {
        _zlib = new ZLibStream(_compressed, CompressionMode.Decompress);
    }

    public int Encoding => 16;

    public void Decode(RfbStream input, Framebuffer framebuffer, ScreenRect rect)
    {
        uint length = input.ReadUInt32();
        if (length > MaxCompressedLength)
            throw new RfbException("The server sent an overlong ZRLE rectangle.");
        _compressed.Append(input.ReadBytes((int)length));

        try
        {
            for (int tileY = rect.Y; tileY < rect.Bottom; tileY += TileSide)
            {
                for (int tileX = rect.X; tileX < rect.Right; tileX += TileSide)
                {
                    ScreenRect bounds = new(tileX, tileY, Math.Min(TileSide, rect.Right - tileX), Math.Min(TileSide, rect.Bottom - tileY));
                    Span<uint> pixels = _tile.AsSpan(0, bounds.Width * bounds.Height);
                    DecodeTile(pixels, bounds);
                    lock (framebuffer.SyncRoot)
                        framebuffer.Write(bounds, pixels);
                }
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException)
        {
            throw new RfbException("The server sent invalid ZRLE data.");
        }
    }

    public void Dispose() => _zlib.Dispose();

    private void DecodeTile(Span<uint> pixels, ScreenRect bounds)
    {
        byte subencoding = ReadByte();
        switch (subencoding)
        {
            case 0:
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = ReadCompactPixel();
                break;
            case 1:
                pixels.Fill(ReadCompactPixel());
                break;
            case <= 16:
                ReadPalette(subencoding);
                ReadPacked(pixels, bounds.Width, subencoding);
                break;
            case 128:
                ReadRuns(pixels, palette: false);
                break;
            case >= 130:
                ReadPalette(subencoding - 128);
                ReadRuns(pixels, palette: true);
                break;
            default:
                throw new RfbException($"The server sent an unknown ZRLE tile type {subencoding}.");
        }
    }

    private void ReadPalette(int size)
    {
        _paletteSize = size;
        for (int i = 0; i < size; i++)
            _palette[i] = ReadCompactPixel();
    }

    // Palette indices of 1, 2 or 4 bits, most significant first; every row starts on a new byte.
    private void ReadPacked(Span<uint> pixels, int width, int paletteSize)
    {
        int bits = paletteSize switch { 2 => 1, <= 4 => 2, _ => 4 };
        int mask = (1 << bits) - 1;
        int height = pixels.Length / width;
        for (int y = 0; y < height; y++)
        {
            int shift = -1;
            byte current = 0;
            for (int x = 0; x < width; x++)
            {
                if (shift < 0)
                {
                    current = ReadByte();
                    shift = 8 - bits;
                }

                int index = (current >> shift) & mask;
                shift -= bits;
                if (index >= paletteSize)
                    throw new RfbException("The server sent a ZRLE palette index outside its palette.");
                pixels[y * width + x] = _palette[index];
            }
        }
    }

    private void ReadRuns(Span<uint> pixels, bool palette)
    {
        int position = 0;
        while (position < pixels.Length)
        {
            uint pixel;
            int run = 1;
            if (palette)
            {
                byte index = ReadByte();
                if ((index & 0x7F) >= _paletteSize)
                    throw new RfbException("The server sent a ZRLE palette index outside its palette.");
                pixel = _palette[index & 0x7F];
                if ((index & 0x80) != 0)
                    run = ReadRunLength();
            }
            else
            {
                pixel = ReadCompactPixel();
                run = ReadRunLength();
            }

            if (run > pixels.Length - position)
                throw new RfbException("The server sent a ZRLE run longer than its tile.");
            pixels.Slice(position, run).Fill(pixel);
            position += run;
        }
    }

    // 255 + 255 + … + last + 1.
    private int ReadRunLength()
    {
        int length = 1;
        byte part;
        do
        {
            part = ReadByte();
            length += part;
            if (length > TileSide * TileSide)
                throw new RfbException("The server sent a ZRLE run longer than its tile.");
        }
        while (part == 255);

        return length;
    }

    // Three bytes (blue, green, red): the client's 32-bit pixels without their padding byte.
    private uint ReadCompactPixel()
    {
        _zlib.ReadExactly(_scratch, 0, 3);
        return (uint)(_scratch[0] | (_scratch[1] << 8) | (_scratch[2] << 16));
    }

    private byte ReadByte()
    {
        int value = _zlib.ReadByte();
        return value < 0 ? throw new EndOfStreamException() : (byte)value;
    }

    /// <summary>The compressed data received so far, read by the zlib stream as it needs it.</summary>
    private sealed class FeedStream : Stream
    {
        private readonly Queue<byte[]> _chunks = new();
        private int _offset;

        public void Append(byte[] data)
        {
            if (data.Length > 0)
                _chunks.Enqueue(data);
        }

        // Returns 0 when everything received so far is read; the zlib stream then asks again next time.
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = 0;
            while (count > 0 && _chunks.TryPeek(out byte[]? chunk))
            {
                int part = Math.Min(count, chunk.Length - _offset);
                Array.Copy(chunk, _offset, buffer, offset, part);
                offset += part;
                count -= part;
                read += part;
                _offset += part;
                if (_offset == chunk.Length)
                {
                    _chunks.Dequeue();
                    _offset = 0;
                }
            }

            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
