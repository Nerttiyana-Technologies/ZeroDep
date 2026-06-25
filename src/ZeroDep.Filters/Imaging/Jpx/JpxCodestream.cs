using System;
using System.Collections.Generic;

namespace ZeroDep.Filters.Jpx;

/// <summary>A JPEG 2000 image component's sample geometry (from the SIZ marker).</summary>
internal sealed class JpxComponent
{
    public int Depth { get; init; }

    public bool Signed { get; init; }

    public int XRsiz { get; init; } = 1;

    public int YRsiz { get; init; } = 1;
}

/// <summary>Image and tile geometry (SIZ, ITU-T T.800 §A.5.1).</summary>
internal sealed class JpxSiz
{
    public int Xsiz { get; init; }

    public int Ysiz { get; init; }

    public int XOsiz { get; init; }

    public int YOsiz { get; init; }

    public int XTsiz { get; init; }

    public int YTsiz { get; init; }

    public int XTOsiz { get; init; }

    public int YTOsiz { get; init; }

    public JpxComponent[] Components { get; init; } = Array.Empty<JpxComponent>();

    public int Width => Xsiz - XOsiz;

    public int Height => Ysiz - YOsiz;

    public int TilesX => (int)(((long)Xsiz - XTOsiz + XTsiz - 1) / XTsiz);

    public int TilesY => (int)(((long)Ysiz - YTOsiz + YTsiz - 1) / YTsiz);
}

/// <summary>Coding style parameters (COD/COC, §A.6.1).</summary>
internal sealed class JpxCod
{
    public int Progression { get; init; }      // 0=LRCP 1=RLCP 2=RPCL 3=PCRL 4=CPRL

    public int Layers { get; init; }

    public bool UseMct { get; init; }           // multi-component transform

    public int DecompositionLevels { get; init; }

    public int CodeBlockWidth { get; init; }    // actual pixels (2^(value+2))

    public int CodeBlockHeight { get; init; }

    public int CodeBlockStyle { get; init; }

    public bool Reversible { get; init; }       // true = 5/3, false = 9/7

    /// <summary>Per-resolution precinct (PPx, PPy) exponents, or null when default (max) precincts.</summary>
    public (int X, int Y)[]? PrecinctSizes { get; init; }
}

/// <summary>Quantization parameters (QCD/QCC, §A.6.4).</summary>
internal sealed class JpxQcd
{
    public int Style { get; init; }             // sqcd & 0x1f: 0=none, 1=scalar derived, 2=scalar expounded

    public int GuardBits { get; init; }

    /// <summary>(exponent, mantissa) per sub-band, in decode order.</summary>
    public (int Exponent, int Mantissa)[] StepSizes { get; init; } = Array.Empty<(int, int)>();
}

/// <summary>A tile-part: its tile index and the byte range of its packed data (between SOD and the next marker).</summary>
internal sealed class JpxTilePart
{
    public int TileIndex { get; init; }

    public int DataStart { get; init; }

    public int DataLength { get; init; }
}

/// <summary>The parsed JPEG 2000 codestream: main-header parameters plus the tile-part data ranges.</summary>
internal sealed class JpxImage
{
    public byte[] Data { get; init; } = Array.Empty<byte>();

    public JpxSiz Siz { get; init; } = new JpxSiz();

    public JpxCod Cod { get; init; } = new JpxCod();

    public JpxQcd Qcd { get; init; } = new JpxQcd();

    public List<JpxTilePart> TileParts { get; } = new List<JpxTilePart>();
}

/// <summary>
/// Parses a JPEG 2000 codestream (raw or JP2-boxed) into a <see cref="JpxImage"/>: SIZ/COD/QCD main
/// header plus tile-part data ranges (ITU-T T.800). Entropy decoding happens in later stages.
/// </summary>
internal static class JpxCodestream
{
    public static JpxImage Parse(byte[] input)
    {
        byte[] data = UnwrapJp2(input);
        int p = 0;
        if (U16(data, p) != 0xFF4F)
        {
            // tolerate leading bytes before SOC
            int soc = IndexOf(data, 0xFF4F);
            if (soc < 0)
            {
                throw new NotSupportedException("Not a JPEG 2000 codestream (no SOC marker).");
            }

            p = soc;
        }

        p += 2; // SOC

        JpxSiz? siz = null;
        JpxCod? cod = null;
        JpxQcd? qcd = null;
        var tileParts = new List<JpxTilePart>();

        while (p + 2 <= data.Length)
        {
            int marker = U16(data, p);
            if (marker == 0xFF90)
            {
                // SOT — tile-part header, then SOD, then packed data up to the tile-part length.
                int lsot = U16(data, p + 2);
                int isot = U16(data, p + 4);
                long psot = U32(data, p + 6);
                int sod = FindSod(data, p + 2 + lsot);
                if (sod < 0)
                {
                    break;
                }

                int dataStart = sod + 2;
                int dataEnd = psot > 0 ? (int)(p + psot) : data.Length;
                if (dataEnd > data.Length || dataEnd <= dataStart)
                {
                    dataEnd = data.Length;
                }

                tileParts.Add(new JpxTilePart
                {
                    TileIndex = isot,
                    DataStart = dataStart,
                    DataLength = Math.Max(0, dataEnd - dataStart),
                });

                p = dataEnd;
                continue;
            }

            if (marker == 0xFFD9)
            {
                break; // EOC
            }

            if (p + 4 > data.Length)
            {
                break;
            }

            int length = U16(data, p + 2);
            int seg = p + 4;
            switch (marker)
            {
                case 0xFF51:
                    siz = ParseSiz(data, seg);
                    break;
                case 0xFF52:
                    cod = ParseCod(data, seg);
                    break;
                case 0xFF5C:
                    qcd = ParseQcd(data, seg, length);
                    break;
                default:
                    break; // COC/QCC/RGN/POC/COM/TLM/PLM/PPM etc. — handled in later stages as needed
            }

            p += 2 + length;
        }

        var image = new JpxImage
        {
            Data = data,
            Siz = siz ?? new JpxSiz(),
            Cod = cod ?? new JpxCod(),
            Qcd = qcd ?? new JpxQcd(),
        };
        image.TileParts.AddRange(tileParts);
        return image;
    }

    private static JpxSiz ParseSiz(byte[] d, int s)
    {
        int csiz = U16(d, s + 34);
        var comps = new JpxComponent[csiz];
        int c = s + 36;
        for (int i = 0; i < csiz; i++)
        {
            int ssiz = d[c];
            comps[i] = new JpxComponent
            {
                Depth = (ssiz & 0x7F) + 1,
                Signed = (ssiz & 0x80) != 0,
                XRsiz = d[c + 1],
                YRsiz = d[c + 2],
            };
            c += 3;
        }

        return new JpxSiz
        {
            Xsiz = (int)U32(d, s + 2),
            Ysiz = (int)U32(d, s + 6),
            XOsiz = (int)U32(d, s + 10),
            YOsiz = (int)U32(d, s + 14),
            XTsiz = (int)U32(d, s + 18),
            YTsiz = (int)U32(d, s + 22),
            XTOsiz = (int)U32(d, s + 26),
            YTOsiz = (int)U32(d, s + 30),
            Components = comps,
        };
    }

    private static JpxCod ParseCod(byte[] d, int s)
    {
        int scod = d[s];
        int progression = d[s + 1];
        int layers = U16(d, s + 2);
        int mct = d[s + 4];
        int levels = d[s + 5];
        int cbw = (d[s + 6] & 0x0F) + 2;
        int cbh = (d[s + 7] & 0x0F) + 2;
        int cbStyle = d[s + 8];
        int transform = d[s + 9];

        (int, int)[]? precincts = null;
        if ((scod & 0x01) != 0)
        {
            precincts = new (int, int)[levels + 1];
            for (int i = 0; i <= levels; i++)
            {
                int b = d[s + 10 + i];
                precincts[i] = (b & 0x0F, (b >> 4) & 0x0F);
            }
        }

        return new JpxCod
        {
            Progression = progression,
            Layers = layers,
            UseMct = mct == 1,
            DecompositionLevels = levels,
            CodeBlockWidth = 1 << cbw,
            CodeBlockHeight = 1 << cbh,
            CodeBlockStyle = cbStyle,
            Reversible = transform == 1,
            PrecinctSizes = precincts,
        };
    }

    private static JpxQcd ParseQcd(byte[] d, int s, int length)
    {
        int sqcd = d[s];
        int style = sqcd & 0x1F;
        int guardBits = sqcd >> 5;
        var steps = new List<(int, int)>();
        int p = s + 1;
        int end = s + length - 2;

        if (style == 0)
        {
            // no quantization: 8-bit exponents
            while (p < end)
            {
                steps.Add((d[p] >> 3, 0));
                p += 1;
            }
        }
        else
        {
            // scalar derived (1) or expounded (2): 16-bit (exponent:5, mantissa:11)
            while (p + 1 < end + 1 && p + 1 <= d.Length - 1 && p < end)
            {
                int v = U16(d, p);
                steps.Add((v >> 11, v & 0x7FF));
                p += 2;
            }
        }

        return new JpxQcd { Style = style, GuardBits = guardBits, StepSizes = steps.ToArray() };
    }

    // Find SOD (0xFF93) at or after offset.
    private static int FindSod(byte[] d, int from)
    {
        for (int i = from; i + 1 < d.Length; i++)
        {
            if (d[i] == 0xFF && d[i + 1] == 0x93)
            {
                return i;
            }
        }

        return -1;
    }

    // If the data is a JP2/JPX box file, return the contiguous codestream (jp2c) payload; else as-is.
    private static byte[] UnwrapJp2(byte[] d)
    {
        if (d.Length < 12 || !(d[0] == 0 && d[1] == 0 && d[2] == 0 && d[3] == 0x0C && d[4] == 0x6A && d[5] == 0x50))
        {
            return d; // not JP2-boxed
        }

        int p = 0;
        while (p + 8 <= d.Length)
        {
            long len = U32(d, p);
            int type = (int)U32(d, p + 4);
            int headerLen = 8;
            long boxLen = len;
            if (len == 1)
            {
                // 64-bit extended length
                boxLen = ((long)U32(d, p + 8) << 32) | U32(d, p + 12);
                headerLen = 16;
            }
            else if (len == 0)
            {
                boxLen = d.Length - p;
            }

            if (type == 0x6A703263) // 'jp2c'
            {
                int start = p + headerLen;
                int end = (int)Math.Min(p + boxLen, d.Length);
                var cs = new byte[end - start];
                Array.Copy(d, start, cs, 0, cs.Length);
                return cs;
            }

            if (boxLen <= 0)
            {
                break;
            }

            p += (int)boxLen;
        }

        return d;
    }

    private static int IndexOf(byte[] d, int marker)
    {
        byte hi = (byte)(marker >> 8), lo = (byte)(marker & 0xFF);
        for (int i = 0; i + 1 < d.Length; i++)
        {
            if (d[i] == hi && d[i + 1] == lo)
            {
                return i;
            }
        }

        return -1;
    }

    private static int U16(byte[] d, int o) => o + 1 < d.Length ? (d[o] << 8) | d[o + 1] : 0;

    private static long U32(byte[] d, int o)
        => o + 3 < d.Length ? ((long)d[o] << 24) | ((long)d[o + 1] << 16) | ((long)d[o + 2] << 8) | d[o + 3] : 0;
}
