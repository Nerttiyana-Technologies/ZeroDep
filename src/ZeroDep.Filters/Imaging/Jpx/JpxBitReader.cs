namespace ZeroDep.Filters.Jpx;

/// <summary>
/// Bit reader for JPEG 2000 packet headers (ITU-T T.800 §B.10.1). Bits are read MSB-first with the
/// codestream bit-unstuffing rule: the byte following a <c>0xFF</c> contributes only its 7 low bits
/// (its MSB is a stuffed zero). Mirrors the OpenJPEG <c>bio</c> read path. A fresh instance is created
/// for each packet header; <see cref="ByteAlign"/> then yields the byte offset where the packet body
/// (code-block data) begins.
/// </summary>
internal sealed class JpxBitReader
{
    private readonly byte[] _data;
    private readonly int _start;
    private readonly int _end;
    private int _bp;
    private int _buf;
    private int _ct;

    public JpxBitReader(byte[] data, int start, int end)
    {
        _data = data;
        _start = start;
        _end = end;
        _bp = start;
        _buf = 0;
        _ct = 0;
    }

    /// <summary>The byte offset one past the bytes consumed by the header (the body start).</summary>
    public int Position => _bp;

    /// <summary>Reads a single bit (MSB-first, with unstuffing).</summary>
    public int ReadBit()
    {
        if (_ct == 0)
        {
            ByteIn();
        }

        _ct--;
        return (_buf >> _ct) & 1;
    }

    /// <summary>Reads <paramref name="n"/> bits as an unsigned integer, MSB first.</summary>
    public int ReadBits(int n)
    {
        int v = 0;
        for (int i = 0; i < n; i++)
        {
            v = (v << 1) | ReadBit();
        }

        return v;
    }

    /// <summary>
    /// Aligns to the next byte boundary at the end of a packet header. If the last buffered byte was
    /// <c>0xFF</c>, the trailing stuffed byte is consumed (T.800 inalign).
    /// </summary>
    public void ByteAlign()
    {
        if ((_buf & 0xFF) == 0xFF)
        {
            ByteIn();
        }

        _ct = 0;
    }

    private void ByteIn()
    {
        // The count of usable bits depends on whether the *previous* byte was 0xFF.
        _ct = _buf == 0xFF ? 7 : 8;
        _buf = _bp < _end ? _data[_bp++] : 0xFF;
    }
}
