using System;
using System.Collections.Generic;

namespace ZeroDep.Filters;

/// <summary>
/// Decodes PDF <c>/LZWDecode</c> data — variable-width LZW (9–12 bit codes) with the PDF default
/// EarlyChange of 1 (ISO 32000-2 §7.4.4.2).
/// </summary>
public static class LzwDecode
{
    private const int ClearTable = 256;
    private const int EndOfData = 257;

    /// <summary>Decodes LZW data and applies an optional PNG/TIFF predictor.</summary>
    /// <param name="input">The compressed bytes.</param>
    /// <param name="predictor">The <c>/Predictor</c> value (1 = none).</param>
    /// <param name="colors">The <c>/Colors</c> value (samples per pixel).</param>
    /// <param name="bitsPerComponent">The <c>/BitsPerComponent</c> value.</param>
    /// <param name="columns">The <c>/Columns</c> value (samples per row).</param>
    /// <param name="earlyChange">The <c>/EarlyChange</c> value (default 1).</param>
    /// <returns>The decoded bytes.</returns>
    public static byte[] Decode(byte[] input, int predictor, int colors, int bitsPerComponent, int columns, int earlyChange)
    {
        byte[] raw = Decode(input, earlyChange);
        return predictor <= 1 ? raw : Predictor.Apply(raw, predictor, colors, bitsPerComponent, columns);
    }

    /// <summary>Decodes LZW data (no predictor).</summary>
    /// <param name="input">The compressed bytes.</param>
    /// <param name="earlyChange">The <c>/EarlyChange</c> value (default 1).</param>
    /// <returns>The decoded bytes.</returns>
    public static byte[] Decode(byte[] input, int earlyChange = 1)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));

        var output = new List<byte>(input.Length * 3);
        var table = new List<byte[]>(4096);
        void Reset()
        {
            table.Clear();
            for (int i = 0; i < 256; i++) table.Add(new[] { (byte)i });
            table.Add(Array.Empty<byte>()); // 256 ClearTable placeholder
            table.Add(Array.Empty<byte>()); // 257 EndOfData placeholder
        }
        Reset();

        int codeWidth = 9;
        int bitBuffer = 0;
        int bitCount = 0;
        int pos = 0;
        byte[]? previous = null;

        int ReadCode()
        {
            while (bitCount < codeWidth)
            {
                if (pos >= input.Length) return -1;
                bitBuffer = (bitBuffer << 8) | input[pos++];
                bitCount += 8;
            }
            bitCount -= codeWidth;
            return (bitBuffer >> bitCount) & ((1 << codeWidth) - 1);
        }

        int code;
        while ((code = ReadCode()) != -1)
        {
            if (code == ClearTable)
            {
                Reset();
                codeWidth = 9;
                previous = null;
                continue;
            }
            if (code == EndOfData) break;

            byte[] entry;
            if (code < table.Count)
            {
                entry = table[code];
            }
            else if (previous is not null)
            {
                entry = new byte[previous.Length + 1];
                Array.Copy(previous, entry, previous.Length);
                entry[previous.Length] = previous[0];
            }
            else
            {
                break; // invalid stream
            }

            output.AddRange(entry);

            if (previous is not null)
            {
                var added = new byte[previous.Length + 1];
                Array.Copy(previous, added, previous.Length);
                added[previous.Length] = entry[0];
                table.Add(added);
            }

            previous = entry;

            if (table.Count + earlyChange >= (1 << codeWidth) && codeWidth < 12)
            {
                codeWidth++;
            }
        }

        return output.ToArray();
    }
}
