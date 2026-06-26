namespace ZeroDep.Fonts;

/// <summary>A bounds-safe big-endian byte reader for SFNT/CFF font tables (reads past the end return 0).</summary>
internal sealed class BigEndianReader
{
    private readonly byte[] _data;

    public BigEndianReader(byte[] data) => _data = data;

    public int Position { get; set; }

    public int Length => _data.Length;

    public byte ReadU8()
    {
        byte v = Position >= 0 && Position < _data.Length ? _data[Position] : (byte)0;
        Position++;
        return v;
    }

    public sbyte ReadS8() => (sbyte)ReadU8();

    public int ReadU16()
    {
        int hi = ReadU8();
        int lo = ReadU8();
        return (hi << 8) | lo;
    }

    public short ReadS16() => (short)ReadU16();

    public uint ReadU32()
    {
        uint a = ReadU8();
        uint b = ReadU8();
        uint c = ReadU8();
        uint d = ReadU8();
        return (a << 24) | (b << 16) | (c << 8) | d;
    }

    public int ReadS32() => (int)ReadU32();

    /// <summary>Reads a signed 2.14 fixed-point value (F2Dot14) used by composite-glyph transforms.</summary>
    public double ReadF2Dot14() => ReadS16() / 16384.0;

    public byte PeekU8(int offset)
    {
        int p = Position + offset;
        return p >= 0 && p < _data.Length ? _data[p] : (byte)0;
    }
}
