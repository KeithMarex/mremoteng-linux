using System.Buffers.Binary;
using System.Text;

namespace mRemoteNG.Vnc;

/// <summary>Reads the RFB protocol's big-endian values from a connection.</summary>
internal sealed class RfbStream(Stream stream)
{
    private readonly BufferedStream _input = new(stream, 64 * 1024);
    private readonly byte[] _scratch = new byte[8];

    public Stream Inner => stream;

    public byte ReadByte()
    {
        int value = _input.ReadByte();
        return value < 0 ? throw new EndOfStreamException() : (byte)value;
    }

    public ushort ReadUInt16()
    {
        _input.ReadExactly(_scratch, 0, 2);
        return BinaryPrimitives.ReadUInt16BigEndian(_scratch);
    }

    public uint ReadUInt32()
    {
        _input.ReadExactly(_scratch, 0, 4);
        return BinaryPrimitives.ReadUInt32BigEndian(_scratch);
    }

    public int ReadInt32() => (int)ReadUInt32();

    /// <summary>A pixel in the client's format: 32 bits, little-endian (B, G, R, padding).</summary>
    public uint ReadPixel()
    {
        _input.ReadExactly(_scratch, 0, 4);
        return BinaryPrimitives.ReadUInt32LittleEndian(_scratch);
    }

    public void ReadExactly(Span<byte> buffer) => _input.ReadExactly(buffer);

    public byte[] ReadBytes(int count)
    {
        byte[] buffer = new byte[count];
        _input.ReadExactly(buffer);
        return buffer;
    }

    public void Skip(int count)
    {
        Span<byte> discard = stackalloc byte[256];
        while (count > 0)
        {
            int part = Math.Min(count, discard.Length);
            _input.ReadExactly(discard[..part]);
            count -= part;
        }
    }

    /// <summary>A length-prefixed string; longer than <paramref name="limit"/> bytes is a protocol error.</summary>
    public string ReadString(int limit, Encoding encoding)
    {
        uint length = ReadUInt32();
        if (length > limit)
            throw new RfbException("The server sent an overlong text.");
        return encoding.GetString(ReadBytes((int)length));
    }
}
