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

    /// <summary>Code-block width exponent (xcb), i.e. log2(<see cref="CodeBlockWidth"/>).</summary>
    public int CodeBlockWidthExp { get; init; }

    /// <summary>Code-block height exponent (ycb), i.e. log2(<see cref="CodeBlockHeight"/>).</summary>
    public int CodeBlockHeightExp { get; init; }

    public int CodeBlockStyle { get; init; }

    public bool Reversible { get; init; }       // true = 5/3, false = 9/7

    /// <summary>SOP marker segments may be present before packets (Scod bit 1).</summary>
    public bool UseSop { get; init; }

    /// <summary>EPH marker may terminate packet headers (Scod bit 2).</summary>
    public bool UseEph { get; init; }

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

/// <summary>Per-tile coding overrides parsed from a tile-part header (COD/QCD/COC/QCC).</summary>
internal sealed class JpxTileCoding
{
    public JpxCod? Cod { get; set; }

    public JpxQcd? Qcd { get; set; }

    public Dictionary<int, JpxCod> ComponentCod { get; } = new Dictionary<int, JpxCod>();

    public Dictionary<int, JpxQcd> ComponentQcd { get; } = new Dictionary<int, JpxQcd>();
}

/// <summary>The parsed JPEG 2000 codestream: main-header parameters plus the tile-part data ranges.</summary>
internal sealed class JpxImage
{
    public byte[] Data { get; init; } = Array.Empty<byte>();

    public JpxSiz Siz { get; init; } = new JpxSiz();

    public JpxCod Cod { get; init; } = new JpxCod();

    public JpxQcd Qcd { get; init; } = new JpxQcd();

    /// <summary>Main-header per-component coding overrides (COC).</summary>
    public Dictionary<int, JpxCod> ComponentCod { get; } = new Dictionary<int, JpxCod>();

    /// <summary>Main-header per-component quantization overrides (QCC).</summary>
    public Dictionary<int, JpxQcd> ComponentQcd { get; } = new Dictionary<int, JpxQcd>();

    /// <summary>Per-tile coding overrides (keyed by tile index).</summary>
    public Dictionary<int, JpxTileCoding> Tiles { get; } = new Dictionary<int, JpxTileCoding>();

    public List<JpxTilePart> TileParts { get; } = new List<JpxTilePart>();

    /// <summary>Resolves the effective coding style for a (tile, component): tile-COC, tile-COD, main-COC, main-COD.</summary>
    public JpxCod CodFor(int tileIndex, int component)
    {
        if (Tiles.TryGetValue(tileIndex, out JpxTileCoding? t))
        {
            if (t.ComponentCod.TryGetValue(component, out JpxCod? tc))
            {
                return tc;
            }

            if (t.Cod is { } td)
            {
                return td;
            }
        }

        return ComponentCod.TryGetValue(component, out JpxCod? mc) ? mc : Cod;
    }

    /// <summary>Resolves the effective quantization for a (tile, component): tile-QCC, tile-QCD, main-QCC, main-QCD.</summary>
    public JpxQcd QcdFor(int tileIndex, int component)
    {
        if (Tiles.TryGetValue(tileIndex, out JpxTileCoding? t))
        {
            if (t.ComponentQcd.TryGetValue(component, out JpxQcd? tq))
            {
                return tq;
            }

            if (t.Qcd is { } td)
            {
                return td;
            }
        }

        return ComponentQcd.TryGetValue(component, out JpxQcd? mq) ? mq : Qcd;
    }
}

/// <summary>
/// Parses a JPEG 2000 codestream (raw or JP2-boxed) into a <see cref="JpxImage"/>: the SIZ/COD/QCD main
/// header (plus COC/QCC overrides and per-tile coding) and the tile-part data ranges (ITU-T T.800).
/// Entropy decoding happens in <see cref="JpxDecode"/>.
/// </summary>
internal static class JpxCodestream
{
    public static JpxImage Parse(byte[] input)
    {
        byte[] data = UnwrapJp2(input);
        int p = 0;
        if (U16(data, p) != 0xFF4F)
        {
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
        var compCod = new Dictionary<int, JpxCod>();
        var compQcd = new Dictionary<int, JpxQcd>();
        var tiles = new Dictionary<int, JpxTileCoding>();
        var tileParts = new List<JpxTilePart>();
        int csiz = 0;

        while (p + 2 <= data.Length)
        {
            int marker = U16(data, p);
            if (marker == 0xFF90)
            {
                // SOT — tile-part header. Parse override markers up to SOD, then record the body range.
                int lsot = U16(data, p + 2);
                int isot = U16(data, p + 4);
                long psot = U32(data, p + 6);

                var tileCoding = GetOrAdd(tiles, isot);
                int hp = p + 2 + lsot;
                int sod = ParseTileHeader(data, hp, csiz, tileCoding);
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
                    csiz = siz.Components.Length;
                    break;
                case 0xFF52:
                    cod = ParseCod(data, seg);
                    break;
                case 0xFF53:
                {
                    int comp = ReadComponentIndex(data, seg, csiz, out int after);
                    compCod[comp] = ParseCoc(data, after, cod);
                    break;
                }

                case 0xFF5C:
                    qcd = ParseQcd(data, seg, length);
                    break;
                case 0xFF5D:
                {
                    int comp = ReadComponentIndex(data, seg, csiz, out int after);
                    compQcd[comp] = ParseQcc(data, after, seg + length);
                    break;
                }

                default:
                    break; // RGN/POC/COM/TLM/PLM/PPM etc. — handled in later stages as needed
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
        foreach (KeyValuePair<int, JpxCod> kv in compCod)
        {
            image.ComponentCod[kv.Key] = kv.Value;
        }

        foreach (KeyValuePair<int, JpxQcd> kv in compQcd)
        {
            image.ComponentQcd[kv.Key] = kv.Value;
        }

        foreach (KeyValuePair<int, JpxTileCoding> kv in tiles)
        {
            image.Tiles[kv.Key] = kv.Value;
        }

        image.TileParts.AddRange(tileParts);
        return image;
    }

    // Parses tile-part header markers (between the SOT segment and SOD) into per-tile overrides.
    // Returns the offset of the SOD marker, or -1 if none found.
    private static int ParseTileHeader(byte[] d, int p, int csiz, JpxTileCoding tile)
    {
        while (p + 2 <= d.Length)
        {
            int marker = U16(d, p);
            if (marker == 0xFF93)
            {
                return p; // SOD
            }

            if (p + 4 > d.Length)
            {
                return -1;
            }

            int length = U16(d, p + 2);
            int seg = p + 4;
            switch (marker)
            {
                case 0xFF52:
                    tile.Cod = ParseCod(d, seg);
                    break;
                case 0xFF53:
                {
                    int comp = ReadComponentIndex(d, seg, csiz, out int after);
                    tile.ComponentCod[comp] = ParseCoc(d, after, tile.Cod);
                    break;
                }

                case 0xFF5C:
                    tile.Qcd = ParseQcd(d, seg, length);
                    break;
                case 0xFF5D:
                {
                    int comp = ReadComponentIndex(d, seg, csiz, out int after);
                    tile.ComponentQcd[comp] = ParseQcc(d, after, seg + length);
                    break;
                }

                default:
                    break;
            }

            p += 2 + length;
        }

        return -1;
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
        int cbwExp = (d[s + 6] & 0x0F) + 2;
        int cbhExp = (d[s + 7] & 0x0F) + 2;
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
            CodeBlockWidth = 1 << cbwExp,
            CodeBlockHeight = 1 << cbhExp,
            CodeBlockWidthExp = cbwExp,
            CodeBlockHeightExp = cbhExp,
            CodeBlockStyle = cbStyle,
            Reversible = transform == 1,
            UseSop = (scod & 0x02) != 0,
            UseEph = (scod & 0x04) != 0,
            PrecinctSizes = precincts,
        };
    }

    // COC shares the COD body layout minus the SGcod (progression/layers/MCT) group.
    private static JpxCod ParseCoc(byte[] d, int s, JpxCod? baseCod)
    {
        int scoc = d[s];
        int levels = d[s + 1];
        int cbwExp = (d[s + 2] & 0x0F) + 2;
        int cbhExp = (d[s + 3] & 0x0F) + 2;
        int cbStyle = d[s + 4];
        int transform = d[s + 5];

        (int, int)[]? precincts = null;
        if ((scoc & 0x01) != 0)
        {
            precincts = new (int, int)[levels + 1];
            for (int i = 0; i <= levels; i++)
            {
                int b = d[s + 6 + i];
                precincts[i] = (b & 0x0F, (b >> 4) & 0x0F);
            }
        }

        return new JpxCod
        {
            Progression = baseCod?.Progression ?? 0,
            Layers = baseCod?.Layers ?? 1,
            UseMct = false,
            DecompositionLevels = levels,
            CodeBlockWidth = 1 << cbwExp,
            CodeBlockHeight = 1 << cbhExp,
            CodeBlockWidthExp = cbwExp,
            CodeBlockHeightExp = cbhExp,
            CodeBlockStyle = cbStyle,
            Reversible = transform == 1,
            UseSop = baseCod?.UseSop ?? false,
            UseEph = baseCod?.UseEph ?? false,
            PrecinctSizes = precincts,
        };
    }

    private static JpxQcd ParseQcd(byte[] d, int s, int length)
        => ParseQuant(d, s, s + length - 2);

    // QCC begins with the component index (already consumed); the remainder matches QCD.
    private static JpxQcd ParseQcc(byte[] d, int s, int end) => ParseQuant(d, s, end);

    private static JpxQcd ParseQuant(byte[] d, int s, int end)
    {
        int sqcd = d[s];
        int style = sqcd & 0x1F;
        int guardBits = sqcd >> 5;
        var steps = new List<(int, int)>();
        int p = s + 1;

        if (style == 0)
        {
            while (p < end && p < d.Length)
            {
                steps.Add((d[p] >> 3, 0));
                p += 1;
            }
        }
        else
        {
            while (p + 1 < end + 1 && p + 1 <= d.Length && p < end)
            {
                int v = U16(d, p);
                steps.Add((v >> 11, v & 0x7FF));
                p += 2;
            }
        }

        return new JpxQcd { Style = style, GuardBits = guardBits, StepSizes = steps.ToArray() };
    }

    // COC/QCC carry a component index whose width depends on the component count (1 byte if <257, else 2).
    private static int ReadComponentIndex(byte[] d, int s, int csiz, out int after)
    {
        if (csiz < 257)
        {
            after = s + 1;
            return d[s];
        }

        after = s + 2;
        return U16(d, s);
    }

    private static JpxTileCoding GetOrAdd(Dictionary<int, JpxTileCoding> tiles, int index)
    {
        if (!tiles.TryGetValue(index, out JpxTileCoding? t))
        {
            t = new JpxTileCoding();
            tiles[index] = t;
        }

        return t;
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
