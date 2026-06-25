namespace ZeroDep.Filters.Jbig2;

/// <summary>
/// A set of adaptive arithmetic-coding contexts (each carries a probability-state index and the
/// current more-probable symbol). One instance is shared across all decode calls that use the same
/// context family (e.g. a generic-region template).
/// </summary>
internal sealed class ArithContext
{
    public ArithContext(int size)
    {
        Index = new byte[size];
        Mps = new byte[size];
    }

    /// <summary>Probability-state index (0–46) per context.</summary>
    public byte[] Index { get; }

    /// <summary>The current more-probable symbol (0/1) per context.</summary>
    public byte[] Mps { get; }
}

/// <summary>
/// Pure-BCL MQ (QM) arithmetic decoder per ITU-T T.88 Annex E (identical to the JPEG 2000 MQ coder).
/// The core entropy primitive for JBIG2 generic, symbol, text, halftone, and refinement regions.
/// </summary>
internal sealed class MqDecoder
{
    // Qe value, NMPS, NLPS, SWITCH — the 47-state probability estimation table (T.88 Table E.1).
    private static readonly int[] Qe =
    {
        0x5601, 0x3401, 0x1801, 0x0AC1, 0x0521, 0x0221, 0x5601, 0x5401, 0x4801, 0x3801,
        0x3001, 0x2401, 0x1C01, 0x1601, 0x5601, 0x5401, 0x5101, 0x4801, 0x3801, 0x3401,
        0x3001, 0x2801, 0x2401, 0x2201, 0x1C01, 0x1801, 0x1601, 0x1401, 0x1201, 0x1101,
        0x0AC1, 0x09C1, 0x08A1, 0x0521, 0x0441, 0x02A1, 0x0221, 0x0141, 0x0111, 0x0085,
        0x0049, 0x0025, 0x0015, 0x0009, 0x0005, 0x0001, 0x5601,
    };

    private static readonly byte[] Nmps =
    {
        1, 2, 3, 4, 5, 38, 7, 8, 9, 10, 11, 12, 13, 29, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24,
        25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 45, 46,
    };

    private static readonly byte[] Nlps =
    {
        1, 6, 9, 12, 29, 33, 6, 14, 14, 14, 17, 18, 20, 21, 14, 14, 15, 16, 17, 18, 19, 19, 20, 21,
        22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 46,
    };

    private static readonly byte[] Switch =
    {
        1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    };

    private readonly byte[] _data;
    private readonly int _end;
    private int _bp;
    private uint _c;
    private uint _a;
    private int _ct;

    public MqDecoder(byte[] data, int start, int end)
    {
        _data = data;
        _end = end;
        _bp = start;

        _c = (uint)B(_bp) << 16;
        ByteIn();
        _c <<= 7;
        _ct -= 7;
        _a = 0x8000;
    }

    public MqDecoder(byte[] data)
        : this(data, 0, data.Length)
    {
    }

    /// <summary>Decodes one binary decision using the given context label.</summary>
    public int Decode(ArithContext cx, int label)
    {
        int i = cx.Index[label];
        int qe = Qe[i];
        _a -= (uint)qe;

        int d;
        if ((_c >> 16) < (uint)qe)
        {
            // LPS exchange.
            if (_a < (uint)qe)
            {
                _a = (uint)qe;
                d = cx.Mps[label];
                cx.Index[label] = Nmps[i];
            }
            else
            {
                _a = (uint)qe;
                d = 1 - cx.Mps[label];
                if (Switch[i] == 1)
                {
                    cx.Mps[label] = (byte)(1 - cx.Mps[label]);
                }

                cx.Index[label] = Nlps[i];
            }

            Renorm();
        }
        else
        {
            _c -= (uint)qe << 16;
            if ((_a & 0x8000) == 0)
            {
                // MPS exchange.
                if (_a < (uint)qe)
                {
                    d = 1 - cx.Mps[label];
                    if (Switch[i] == 1)
                    {
                        cx.Mps[label] = (byte)(1 - cx.Mps[label]);
                    }

                    cx.Index[label] = Nlps[i];
                }
                else
                {
                    d = cx.Mps[label];
                    cx.Index[label] = Nmps[i];
                }

                Renorm();
            }
            else
            {
                d = cx.Mps[label];
            }
        }

        return d;
    }

    private void Renorm()
    {
        do
        {
            if (_ct == 0)
            {
                ByteIn();
            }

            _a <<= 1;
            _c <<= 1;
            _ct--;
        }
        while ((_a & 0x8000) == 0);
    }

    private void ByteIn()
    {
        if (B(_bp) == 0xFF)
        {
            if (B(_bp + 1) > 0x8F)
            {
                _c += 0xFF00;
                _ct = 8;
            }
            else
            {
                _bp++;
                _c += (uint)B(_bp) << 9;
                _ct = 7;
            }
        }
        else
        {
            _bp++;
            _c += (uint)B(_bp) << 8;
            _ct = 8;
        }
    }

    // Byte at an offset, with 0xFF padding past the end (the MQ convention for marker handling).
    private int B(int index) => index < _end && index >= 0 ? _data[index] : 0xFF;
}
