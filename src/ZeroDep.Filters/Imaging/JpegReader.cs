using System;
using System.Collections.Generic;
using System.IO;

namespace ZeroDep.Filters;

/// <summary>
/// Parses a JPEG (<c>/DCTDecode</c>) stream's headers into <see cref="JpegMetadata"/> (ITU-T T.81).
/// This is the container/frame parser: it reads markers up to the start of scan and does <b>not</b>
/// decode entropy-coded pixel data. Use it to validate declared image dimensions and to feed the
/// pixel decoder (a later stage).
/// </summary>
public static class JpegReader
{
    /// <summary>Reads the structural metadata from a JPEG byte stream.</summary>
    /// <param name="data">The raw JPEG bytes (a <c>/DCTDecode</c> image stream).</param>
    /// <returns>The parsed metadata.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="InvalidDataException">The data is not a valid/parseable JPEG header.</exception>
    public static JpegMetadata ReadMetadata(byte[] data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (data.Length < 2 || data[0] != 0xFF || data[1] != 0xD8)
        {
            throw new InvalidDataException("Not a JPEG: missing SOI marker.");
        }

        int pos = 2;
        int width = 0, height = 0, precision = 0, restartInterval = 0;
        int adobeTransform = -1;
        JpegMode mode = JpegMode.Unsupported;
        bool frameSeen = false;
        var components = new List<JpegComponent>();
        var quant = new Dictionary<int, int[]>();
        var huffman = new List<JpegHuffmanTable>();

        while (pos + 1 < data.Length)
        {
            if (data[pos] != 0xFF)
            {
                pos++;
                continue;
            }

            int marker = data[pos + 1];
            pos += 2;

            if (marker == 0xFF)
            {
                pos--;          // a run of 0xFF fill bytes precedes the real marker
                continue;
            }

            // Standalone markers carry no length: SOI, EOI, RSTn, TEM.
            if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0x01)
            {
                continue;
            }

            if (pos + 2 > data.Length)
            {
                break;
            }

            int length = (data[pos] << 8) | data[pos + 1];
            if (length < 2 || pos + length > data.Length)
            {
                throw new InvalidDataException("JPEG segment length out of range.");
            }

            int segStart = pos + 2;
            int segEnd = pos + length;
            bool startOfScan = false;

            switch (marker)
            {
                case 0xC0: case 0xC1: case 0xC2: case 0xC3:
                case 0xC5: case 0xC6: case 0xC7:
                case 0xC9: case 0xCA: case 0xCB:
                case 0xCD: case 0xCE: case 0xCF:
                    ParseFrame(data, segStart, segEnd, ref precision, ref height, ref width, components);
                    mode = ModeOf(marker);
                    frameSeen = true;
                    break;
                case 0xDB:
                    ParseQuant(data, segStart, segEnd, quant);
                    break;
                case 0xC4:
                    ParseHuffman(data, segStart, segEnd, huffman);
                    break;
                case 0xDD:
                    if (segEnd - segStart >= 2)
                    {
                        restartInterval = (data[segStart] << 8) | data[segStart + 1];
                    }

                    break;
                case 0xEE:
                    adobeTransform = ParseAdobe(data, segStart, segEnd, adobeTransform);
                    break;
                case 0xDA:
                    startOfScan = true;     // metadata complete — stop before entropy data
                    break;
                default:
                    break;                  // other APPn, COM segments are skipped
            }

            pos = segEnd;
            if (startOfScan)
            {
                break;
            }
        }

        if (!frameSeen)
        {
            throw new InvalidDataException("JPEG has no frame header (SOFn).");
        }

        return new JpegMetadata
        {
            Width = width,
            Height = height,
            Precision = precision,
            Mode = mode,
            Components = components,
            RestartInterval = restartInterval,
            AdobeTransform = adobeTransform,
            QuantizationTables = quant,
            HuffmanTables = huffman,
        };
    }

    private static int ParseAdobe(byte[] d, int s, int e, int current)
    {
        // APP14 "Adobe" segment: 'Adobe' (5) + version(2) + flags0(2) + flags1(2) + transform(1).
        if (e - s < 12 || d[s] != 0x41 || d[s + 1] != 0x64 || d[s + 2] != 0x6F || d[s + 3] != 0x62 || d[s + 4] != 0x65)
        {
            return current;
        }

        return d[s + 11];
    }

    private static JpegMode ModeOf(int marker)
    {
        switch (marker)
        {
            case 0xC0: return JpegMode.Baseline;
            case 0xC1: return JpegMode.ExtendedSequential;
            case 0xC2: return JpegMode.Progressive;
            default: return JpegMode.Unsupported;
        }
    }

    private static void ParseFrame(byte[] d, int s, int e, ref int precision, ref int height, ref int width, List<JpegComponent> components)
    {
        if (e - s < 6)
        {
            throw new InvalidDataException("Truncated JPEG frame header.");
        }

        precision = d[s];
        height = (d[s + 1] << 8) | d[s + 2];
        width = (d[s + 3] << 8) | d[s + 4];
        int count = d[s + 5];

        components.Clear();
        int p = s + 6;
        for (int i = 0; i < count; i++)
        {
            if (p + 3 > e)
            {
                throw new InvalidDataException("Truncated JPEG component specification.");
            }

            int sampling = d[p + 1];
            components.Add(new JpegComponent
            {
                Id = d[p],
                HorizontalSampling = (sampling >> 4) & 0x0F,
                VerticalSampling = sampling & 0x0F,
                QuantizationTableId = d[p + 2],
            });
            p += 3;
        }
    }

    private static void ParseQuant(byte[] d, int s, int e, Dictionary<int, int[]> quant)
    {
        int p = s;
        while (p < e)
        {
            int pqtq = d[p++];
            int sixteenBit = (pqtq >> 4) & 0x0F;   // 0 = 8-bit, 1 = 16-bit
            int id = pqtq & 0x0F;

            var table = new int[64];
            for (int i = 0; i < 64; i++)
            {
                if (sixteenBit == 0)
                {
                    if (p >= e)
                    {
                        return;
                    }

                    table[i] = d[p++];
                }
                else
                {
                    if (p + 1 >= e)
                    {
                        return;
                    }

                    table[i] = (d[p] << 8) | d[p + 1];
                    p += 2;
                }
            }

            quant[id] = table;
        }
    }

    private static void ParseHuffman(byte[] d, int s, int e, List<JpegHuffmanTable> huffman)
    {
        int p = s;
        while (p < e)
        {
            int tcth = d[p++];
            var counts = new byte[16];
            int total = 0;
            for (int i = 0; i < 16; i++)
            {
                if (p >= e)
                {
                    return;
                }

                counts[i] = d[p++];
                total += counts[i];
            }

            if (p + total > e)
            {
                total = e - p;
            }

            var symbols = new byte[total];
            Array.Copy(d, p, symbols, 0, total);
            p += total;

            huffman.Add(new JpegHuffmanTable
            {
                TableClass = (tcth >> 4) & 0x0F,
                Id = tcth & 0x0F,
                CodeLengthCounts = counts,
                Symbols = symbols,
            });
        }
    }
}
