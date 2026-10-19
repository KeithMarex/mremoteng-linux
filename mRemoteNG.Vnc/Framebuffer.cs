namespace mRemoteNG.Vnc;

/// <summary>A rectangle of the remote screen, in pixels.</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>The smallest rectangle containing both.</summary>
    public ScreenRect Union(ScreenRect other)
    {
        if (IsEmpty)
            return other;
        if (other.IsEmpty)
            return this;
        int x = Math.Min(X, other.X);
        int y = Math.Min(Y, other.Y);
        return new ScreenRect(x, y, Math.Max(Right, other.Right) - x, Math.Max(Bottom, other.Bottom) - y);
    }
}

/// <summary>
/// The remote screen as 32-bit pixels, laid out as BGRA in memory (the fourth byte is padding, not alpha). The
/// client draws into it while reading updates; the app copies it out under <see cref="SyncRoot"/>.
/// </summary>
public sealed class Framebuffer
{
    /// <summary>The largest screen accepted from a server, against absurd sizes.</summary>
    public const int MaxSide = 16384;

    public Framebuffer(int width, int height)
    {
        if (width is < 1 or > MaxSide || height is < 1 or > MaxSide)
            throw new RfbException($"The server's screen size {width}×{height} is not supported.");
        Width = width;
        Height = height;
        Pixels = new uint[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The pixels, row by row; each is B | G &lt;&lt; 8 | R &lt;&lt; 16.</summary>
    public uint[] Pixels { get; }

    /// <summary>Hold this while reading or writing <see cref="Pixels"/>.</summary>
    public Lock SyncRoot { get; } = new();

    /// <exception cref="RfbException">The rectangle is not inside the screen.</exception>
    public void Check(ScreenRect rect)
    {
        if (rect.X < 0 || rect.Y < 0 || rect.Width < 0 || rect.Height < 0 || rect.Right > Width || rect.Bottom > Height)
            throw new RfbException($"The server sent a rectangle outside its screen: {rect}.");
    }

    public void Fill(ScreenRect rect, uint pixel)
    {
        for (int row = rect.Y; row < rect.Bottom; row++)
            Pixels.AsSpan(row * Width + rect.X, rect.Width).Fill(pixel);
    }

    /// <summary>Writes <paramref name="source"/>, <paramref name="rect"/>'s pixels row by row.</summary>
    public void Write(ScreenRect rect, ReadOnlySpan<uint> source)
    {
        for (int row = 0; row < rect.Height; row++)
            source.Slice(row * rect.Width, rect.Width).CopyTo(Pixels.AsSpan((rect.Y + row) * Width + rect.X, rect.Width));
    }

    /// <summary>Copies the pixels at (<paramref name="sourceX"/>, <paramref name="sourceY"/>) to <paramref name="target"/>.</summary>
    public void Copy(int sourceX, int sourceY, ScreenRect target)
    {
        Check(new ScreenRect(sourceX, sourceY, target.Width, target.Height));
        // Rows are copied in an order that never overwrites a source row before it is read.
        bool upwards = sourceY < target.Y;
        for (int i = 0; i < target.Height; i++)
        {
            int row = upwards ? target.Height - 1 - i : i;
            Span<uint> from = Pixels.AsSpan((sourceY + row) * Width + sourceX, target.Width);
            from.CopyTo(Pixels.AsSpan((target.Y + row) * Width + target.X, target.Width));
        }
    }
}

/// <summary>The server broke the RFB protocol, or the session cannot go on; the message is shown to the user.</summary>
public sealed class RfbException(string message) : Exception(message);
