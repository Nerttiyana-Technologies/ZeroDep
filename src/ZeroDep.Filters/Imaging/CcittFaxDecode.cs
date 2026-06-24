using System;
using System.Collections.Generic;

namespace ZeroDep.Filters;

/// <summary>
/// Pure-BCL decoder for PDF <c>/CCITTFaxDecode</c> bi-level images (ITU-T T.4 / T.6). Supports
/// <b>Group 4</b> (pure two-dimensional, <c>K &lt; 0</c>), <b>Group 3 one-dimensional</b> (<c>K = 0</c>,
/// Modified Huffman), and <b>Group 3 two-dimensional</b> (<c>K &gt; 0</c>, Modified READ with EOL/tag
/// bits). Output is a 1-component (grayscale) <see cref="RasterImage"/> with black pixels as 0 and white
/// as 255 (inverted when <see cref="CcittParams.BlackIs1"/> is set), ready for OCR.
/// </summary>
public static class CcittFaxDecode
{
    private const byte White = 255;
    private const byte Black = 0;

    private enum Mode
    {
        Pass,
        Horizontal,
        Vertical,
        EndOfData,
        Error,
    }

    /// <summary>Decodes a CCITT-encoded stream into a bi-level raster.</summary>
    /// <param name="data">The raw CCITT-encoded bytes.</param>
    /// <param name="parms">Decode parameters from the image's <c>/DecodeParms</c>.</param>
    public static RasterImage Decode(byte[] data, CcittParams parms)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (parms is null)
        {
            throw new ArgumentNullException(nameof(parms));
        }

        int columns = parms.Columns > 0 ? parms.Columns : 1728;
        int maxRows = parms.Rows > 0 ? parms.Rows : int.MaxValue;

        var reader = new BitReader(data);
        var rows = new List<int[]>();      // each row stored as its changing-element positions
        int[] reference = Array.Empty<int>(); // first reference line is all-white (no changes)

        while (rows.Count < maxRows)
        {
            if (parms.EncodedByteAlign)
            {
                reader.AlignToByte();
            }

            // Group 4 (K<0): every row is 2D. Group 3 (K>=0): rows may be preceded by an end-of-line,
            // and for K>0 a tag bit selects 1D (1) vs 2D (0) for the next row.
            bool rowIs2D;
            if (parms.K < 0)
            {
                rowIs2D = true;
            }
            else
            {
                ConsumeEndOfLine(reader);
                if (reader.Eof)
                {
                    break;
                }

                if (parms.K == 0)
                {
                    rowIs2D = false;
                }
                else if (reader.TryReadBit(out int tag))
                {
                    rowIs2D = tag == 0;
                }
                else
                {
                    break;
                }
            }

            if (reader.Eof)
            {
                break;
            }

            int[]? coding = rowIs2D
                ? DecodeRow2D(reader, reference, columns)
                : DecodeRow1D(reader, columns);
            if (coding is null)
            {
                break; // EOFB / RTC / end of data
            }

            rows.Add(coding);
            reference = coding;
        }

        return Expand(rows, columns, parms.BlackIs1);
    }

    // Decodes one Group-4 (2D) row into its changing-element positions, or null at end-of-data.
    private static int[]? DecodeRow2D(BitReader reader, int[] reference, int columns)
    {
        var changes = new List<int>();
        int a0 = -1;
        int color = 0; // 0 = white, 1 = black

        while (a0 < columns)
        {
            // b1 = first changing element on the reference line right of a0 and of colour opposite to a0;
            // b2 = the next changing element after b1.
            int i = 0;
            while (i < reference.Length && !(reference[i] > a0 && (i % 2) == color))
            {
                i++;
            }

            int b1 = i < reference.Length ? reference[i] : columns;
            int b2 = (i + 1) < reference.Length ? reference[i + 1] : columns;

            (Mode mode, int delta) = ReadMode(reader);
            if (mode == Mode.EndOfData || mode == Mode.Error)
            {
                if (changes.Count == 0)
                {
                    return null;
                }

                break;
            }

            switch (mode)
            {
                case Mode.Pass:
                    a0 = b2; // colour continues; no changing element recorded
                    break;

                case Mode.Horizontal:
                {
                    int start = a0 < 0 ? 0 : a0;
                    int r1 = ReadRun(reader, color);
                    int r2 = ReadRun(reader, 1 - color);
                    if (r1 < 0 || r2 < 0)
                    {
                        return changes.Count == 0 ? null : changes.ToArray();
                    }

                    int h1 = Math.Min(start + r1, columns);
                    int h2 = Math.Min(h1 + r2, columns);
                    changes.Add(h1);
                    changes.Add(h2);
                    a0 = h2; // two transitions → colour unchanged
                    break;
                }

                case Mode.Vertical:
                {
                    int a1 = b1 + delta;
                    if (a1 < 0)
                    {
                        a1 = 0;
                    }

                    if (a1 > columns)
                    {
                        a1 = columns;
                    }

                    changes.Add(a1);
                    a0 = a1;
                    color ^= 1;
                    break;
                }
            }
        }

        return changes.ToArray();
    }

    // Decodes one Group-3 one-dimensional (Modified Huffman) row into its changing-element positions.
    private static int[]? DecodeRow1D(BitReader reader, int columns)
    {
        var changes = new List<int>();
        int pos = 0;
        int color = 0; // alternating runs start with white

        while (pos < columns)
        {
            int run = ReadRun(reader, color);
            if (run < 0)
            {
                return changes.Count == 0 ? null : changes.ToArray();
            }

            pos = Math.Min(pos + run, columns);
            changes.Add(pos);
            color ^= 1;
        }

        return changes.ToArray();
    }

    // Consumes an end-of-line code (>= 11 zero bits then a 1) if one is next; otherwise leaves the
    // reader untouched. The >= 11 threshold cleanly separates EOL/fill from any run or mode code, none
    // of which carry that many leading zeros.
    private static void ConsumeEndOfLine(BitReader reader)
    {
        (int Byte, int Bit) save = reader.Save();
        int zeros = 0;

        while (reader.TryReadBit(out int b))
        {
            if (b == 0)
            {
                zeros++;
            }
            else if (zeros >= 11)
            {
                return; // EOL consumed
            }
            else
            {
                break; // a 1 arrived too early — not an EOL
            }
        }

        reader.Restore(save);
    }

    // Reads a 2D mode code (T.6 Table 1); delta is the vertical offset for Vertical modes.
    private static (Mode Mode, int Delta) ReadMode(BitReader reader)
    {
        if (!reader.TryReadBit(out int b))
        {
            return (Mode.EndOfData, 0);
        }

        if (b == 1)
        {
            return (Mode.Vertical, 0); // 1 = V0
        }

        if (!reader.TryReadBit(out b))
        {
            return (Mode.EndOfData, 0);
        }

        if (b == 1)
        {
            // 01x : VR1 (011) / VL1 (010)
            return reader.TryReadBit(out int v1) ? (Mode.Vertical, v1 == 1 ? 1 : -1) : (Mode.EndOfData, 0);
        }

        if (!reader.TryReadBit(out b))
        {
            return (Mode.EndOfData, 0);
        }

        if (b == 1)
        {
            return (Mode.Horizontal, 0); // 001
        }

        if (!reader.TryReadBit(out b))
        {
            return (Mode.EndOfData, 0);
        }

        if (b == 1)
        {
            return (Mode.Pass, 0); // 0001
        }

        if (!reader.TryReadBit(out b))
        {
            return (Mode.EndOfData, 0);
        }

        if (b == 1)
        {
            // 00001x : VR2 (000011) / VL2 (000010)
            return reader.TryReadBit(out int v2) ? (Mode.Vertical, v2 == 1 ? 2 : -2) : (Mode.EndOfData, 0);
        }

        if (!reader.TryReadBit(out b))
        {
            return (Mode.EndOfData, 0);
        }

        if (b == 1)
        {
            // 000001x : VR3 (0000011) / VL3 (0000010)
            return reader.TryReadBit(out int v3) ? (Mode.Vertical, v3 == 1 ? 3 : -3) : (Mode.EndOfData, 0);
        }

        // 000000... : EOL / EOFB / 2D extension — treated as end of data here.
        return (Mode.EndOfData, 0);
    }

    // Reads one run length (a chain of make-up codes ending in a terminating code) for the given colour.
    private static int ReadRun(BitReader reader, int color)
    {
        IReadOnlyDictionary<int, int> table = color == 0 ? CcittTables.White : CcittTables.Black;
        int total = 0;

        while (true)
        {
            int run = ReadCode(reader, table);
            if (run < 0)
            {
                return -1;
            }

            total += run;
            if (run < 64)
            {
                return total; // terminating code
            }
            // make-up code (multiple of 64): keep reading
        }
    }

    private static int ReadCode(BitReader reader, IReadOnlyDictionary<int, int> table)
    {
        int code = 0;
        for (int bits = 1; bits <= CcittTables.MaxCodeBits; bits++)
        {
            if (!reader.TryReadBit(out int bit))
            {
                return -1;
            }

            code = (code << 1) | bit;
            if (table.TryGetValue(CcittTables.Key(bits, code), out int run))
            {
                return run;
            }
        }

        return -1; // no code matched within the maximum length
    }

    private static RasterImage Expand(List<int[]> rows, int columns, bool blackIs1)
    {
        byte white = blackIs1 ? Black : White;
        byte black = blackIs1 ? White : Black;

        int height = rows.Count;
        var samples = new byte[columns * height];

        for (int y = 0; y < height; y++)
        {
            int[] changes = rows[y];
            int rowStart = y * columns;
            int pos = 0;
            int color = 0; // white

            foreach (int change in changes)
            {
                int end = Math.Min(change, columns);
                byte value = color == 0 ? white : black;
                for (int x = pos; x < end; x++)
                {
                    samples[rowStart + x] = value;
                }

                pos = end;
                color ^= 1;
            }

            byte tail = color == 0 ? white : black;
            for (int x = pos; x < columns; x++)
            {
                samples[rowStart + x] = tail;
            }
        }

        return new RasterImage
        {
            Width = columns,
            Height = height,
            Components = 1,
            Samples = samples,
        };
    }

    /// <summary>MSB-first bit reader over a byte buffer.</summary>
    private sealed class BitReader
    {
        private readonly byte[] _data;
        private int _byte;
        private int _bit; // 0..7, MSB first

        public BitReader(byte[] data) => _data = data;

        public bool Eof => _byte >= _data.Length;

        public bool TryReadBit(out int bit)
        {
            if (_byte >= _data.Length)
            {
                bit = 0;
                return false;
            }

            bit = (_data[_byte] >> (7 - _bit)) & 1;
            if (++_bit == 8)
            {
                _bit = 0;
                _byte++;
            }

            return true;
        }

        public void AlignToByte()
        {
            if (_bit != 0)
            {
                _bit = 0;
                _byte++;
            }
        }

        public (int Byte, int Bit) Save() => (_byte, _bit);

        public void Restore((int Byte, int Bit) state)
        {
            _byte = state.Byte;
            _bit = state.Bit;
        }
    }
}
