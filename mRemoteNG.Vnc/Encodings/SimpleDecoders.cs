using System.Buffers;
using System.Runtime.InteropServices;

namespace mRemoteNG.Vnc.Encodings;

/// <summary>Decodes one rectangle of a framebuffer update in its encoding, reading it from the server.</summary>
internal interface IRfbDecoder
{
    /// <summary>The RFB encoding number.</summary>
    int Encoding { get; }

    void Decode(RfbStream input, Framebuffer framebuffer, ScreenRect rect);
}

/// <summary>Raw (encoding 0): every pixel, row by row.</summary>
internal sealed class RawDecoder : IRfbDecoder
{
    public int Encoding => 0;

    public void Decode(RfbStream input, Framebuffer framebuffer, ScreenRect rect)
    {
        byte[] row = ArrayPool<byte>.Shared.Rent(rect.Width * 4);
        try
        {
            for (int y = 0; y < rect.Height; y++)
            {
                input.ReadExactly(row.AsSpan(0, rect.Width * 4));
                ReadOnlySpan<uint> pixels = MemoryMarshal.Cast<byte, uint>(row.AsSpan(0, rect.Width * 4));
                lock (framebuffer.SyncRoot)
                    framebuffer.Write(rect with { Y = rect.Y + y, Height = 1 }, pixels);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(row);
        }
    }
}

/// <summary>CopyRect (encoding 1): the rectangle is a copy of another part of the screen.</summary>
internal sealed class CopyRectDecoder : IRfbDecoder
{
    public int Encoding => 1;

    public void Decode(RfbStream input, Framebuffer framebuffer, ScreenRect rect)
    {
        int sourceX = input.ReadUInt16();
        int sourceY = input.ReadUInt16();
        lock (framebuffer.SyncRoot)
            framebuffer.Copy(sourceX, sourceY, rect);
    }
}

/// <summary>RRE (encoding 2): a background colour with solid sub-rectangles on it.</summary>
internal sealed class RreDecoder : IRfbDecoder
{
    // Against a server that announces more sub-rectangles than pixels.
    private const long MaxSubrectangles = Framebuffer.MaxSide * (long)Framebuffer.MaxSide;

    public int Encoding => 2;

    public void Decode(RfbStream input, Framebuffer framebuffer, ScreenRect rect)
    {
        uint count = input.ReadUInt32();
        if (count > MaxSubrectangles)
            throw new RfbException("The server sent an invalid RRE rectangle.");
        uint background = input.ReadPixel();
        lock (framebuffer.SyncRoot)
            framebuffer.Fill(rect, background);

        for (uint i = 0; i < count; i++)
        {
            uint pixel = input.ReadPixel();
            ScreenRect sub = new(rect.X + input.ReadUInt16(), rect.Y + input.ReadUInt16(), input.ReadUInt16(), input.ReadUInt16());
            if (sub.Right > rect.Right || sub.Bottom > rect.Bottom)
                throw new RfbException("The server sent an RRE sub-rectangle outside its rectangle.");
            lock (framebuffer.SyncRoot)
                framebuffer.Fill(sub, pixel);
        }
    }
}

/// <summary>Hextile (encoding 5): 16×16 tiles, each raw or a background with (coloured) sub-rectangles.</summary>
internal sealed class HextileDecoder : IRfbDecoder
{
    private const int TileSide = 16;
    private const byte RawTile = 1;
    private const byte BackgroundSpecified = 2;
    private const byte ForegroundSpecified = 4;
    private const byte AnySubrects = 8;
    private const byte SubrectsColoured = 16;

    public int Encoding => 5;

    public void Decode(RfbStream input, Framebuffer framebuffer, ScreenRect rect)
    {
        uint[] tile = new uint[TileSide * TileSide];
        uint background = 0;
        uint foreground = 0;
        for (int tileY = rect.Y; tileY < rect.Bottom; tileY += TileSide)
        {
            for (int tileX = rect.X; tileX < rect.Right; tileX += TileSide)
            {
                ScreenRect bounds = new(tileX, tileY, Math.Min(TileSide, rect.Right - tileX), Math.Min(TileSide, rect.Bottom - tileY));
                Span<uint> pixels = tile.AsSpan(0, bounds.Width * bounds.Height);
                byte flags = input.ReadByte();
                if ((flags & RawTile) != 0)
                {
                    for (int i = 0; i < pixels.Length; i++)
                        pixels[i] = input.ReadPixel();
                }
                else
                {
                    if ((flags & BackgroundSpecified) != 0)
                        background = input.ReadPixel();
                    if ((flags & ForegroundSpecified) != 0)
                        foreground = input.ReadPixel();
                    pixels.Fill(background);
                    if ((flags & AnySubrects) != 0)
                        ReadSubrectangles(input, pixels, bounds, (flags & SubrectsColoured) != 0, foreground);
                }

                lock (framebuffer.SyncRoot)
                    framebuffer.Write(bounds, pixels);
            }
        }
    }

    private static void ReadSubrectangles(RfbStream input, Span<uint> pixels, ScreenRect bounds, bool coloured, uint foreground)
    {
        int count = input.ReadByte();
        for (int i = 0; i < count; i++)
        {
            uint pixel = coloured ? input.ReadPixel() : foreground;
            byte position = input.ReadByte();
            byte size = input.ReadByte();
            int x = position >> 4;
            int y = position & 0x0F;
            int width = (size >> 4) + 1;
            int height = (size & 0x0F) + 1;
            if (x + width > bounds.Width || y + height > bounds.Height)
                throw new RfbException("The server sent a Hextile sub-rectangle outside its tile.");
            for (int row = y; row < y + height; row++)
                pixels.Slice(row * bounds.Width + x, width).Fill(pixel);
        }
    }
}
