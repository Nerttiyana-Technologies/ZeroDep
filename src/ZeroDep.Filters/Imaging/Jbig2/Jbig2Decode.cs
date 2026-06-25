using System;
using System.Collections.Generic;

namespace ZeroDep.Filters.Jbig2;

/// <summary>
/// Pure-BCL decoder for PDF <c>/JBIG2Decode</c> bi-level images (ITU-T T.88), embedded organization.
/// Decodes <b>generic regions</b> (arithmetic via the MQ coder, and MMR via the CCITT G4 path),
/// <b>symbol dictionaries</b>, and <b>text regions</b> (arithmetic variants), compositing them onto the
/// page. Halftone and refinement regions (absent from real PDFs) and the Huffman variants are skipped.
/// Output is a 1-component grayscale <see cref="RasterImage"/> (black = 0, white = 255).
/// </summary>
public static class Jbig2Decode
{
    private const int SymbolDictionary = 0;
    private const int IntermediateTextRegion = 4;
    private const int ImmediateTextRegion = 6;
    private const int ImmediateLosslessTextRegion = 7;
    private const int IntermediateGenericRegion = 36;
    private const int ImmediateGenericRegion = 38;
    private const int ImmediateLosslessGenericRegion = 39;
    private const int PageInfo = 48;

    /// <summary>Decodes a JBIG2 image to a raster of the given pixel dimensions (from the PDF image dict).</summary>
    /// <param name="data">The embedded JBIG2 segment stream.</param>
    /// <param name="globals">The optional decoded <c>JBIG2Globals</c> segment stream, or null.</param>
    /// <param name="width">The image width (PDF <c>/Width</c>).</param>
    /// <param name="height">The image height (PDF <c>/Height</c>).</param>
    public static RasterImage Decode(byte[] data, byte[]? globals, int width, int height)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        var page = new Jbig2Bitmap(width, height);
        var symbolDicts = new Dictionary<long, List<Jbig2Bitmap>>();

        if (globals is { Length: > 0 })
        {
            ProcessSegments(globals, page, symbolDicts);
        }

        ProcessSegments(data, page, symbolDicts);

        return ToRaster(page);
    }

    /// <summary>
    /// Inspects which JBIG2 features a stream uses, so corpus coverage can be quantified. A stream is
    /// fully supported when it uses no Huffman coding, no refinement, and no halftone/pattern regions.
    /// </summary>
    public static Jbig2Capabilities Inspect(byte[] data, byte[]? globals = null)
    {
        var caps = default(Jbig2Capabilities);
        InspectStream(globals, ref caps);
        InspectStream(data, ref caps);
        return caps;
    }

    private static void InspectStream(byte[]? d, ref Jbig2Capabilities caps)
    {
        if (d is null)
        {
            return;
        }

        int pos = 0;
        while (pos + 11 <= d.Length)
        {
            if (!TryParseHeader(d, pos, out Segment seg))
            {
                break;
            }

            int s = seg.DataStart;
            switch (seg.Type)
            {
                case SymbolDictionary when s + 2 <= d.Length:
                {
                    int f = (d[s] << 8) | d[s + 1];
                    if ((f & 1) != 0)
                    {
                        caps.UsesHuffman = true;
                    }

                    if ((f & 2) != 0)
                    {
                        caps.UsesRefinement = true; // SDREFAGG
                    }

                    break;
                }

                case IntermediateTextRegion:
                case ImmediateTextRegion:
                case ImmediateLosslessTextRegion when s + 19 <= d.Length:
                {
                    caps.HasText = true;
                    if (s + 19 <= d.Length)
                    {
                        int f = (d[s + 17] << 8) | d[s + 18];
                        if ((f & 1) != 0)
                        {
                            caps.UsesHuffman = true;
                        }

                        if ((f & 2) != 0)
                        {
                            caps.UsesRefinement = true; // SBREFINE
                        }
                    }

                    break;
                }

                case IntermediateGenericRegion:
                case ImmediateGenericRegion:
                case ImmediateLosslessGenericRegion:
                    caps.HasGeneric = true;
                    break;

                case 16:        // pattern dictionary
                case 20:        // intermediate halftone
                case 22:        // immediate halftone
                case 23:        // immediate lossless halftone
                    caps.UsesHalftone = true;
                    break;

                case 40:        // intermediate refinement region
                case 42:        // immediate refinement region
                case 43:        // immediate lossless refinement region
                    caps.UsesRefinement = true;
                    break;

                default:
                    break;
            }

            long next = seg.DataStart + seg.DataLength;
            if (seg.DataLength < 0 || next > d.Length || next <= pos)
            {
                break;
            }

            pos = (int)next;
        }
    }

    /// <summary>Returns the sequence of segment types in a JBIG2 stream (diagnostic; T.88 §7.3 codes).</summary>
    public static IReadOnlyList<int> SegmentTypes(byte[] data)
    {
        var types = new List<int>();
        if (data is null)
        {
            return types;
        }

        int pos = 0;
        while (pos + 11 <= data.Length)
        {
            if (!TryParseHeader(data, pos, out Segment seg))
            {
                break;
            }

            types.Add(seg.Type);
            long next = seg.DataStart + seg.DataLength;
            if (seg.DataLength < 0 || next > data.Length || next <= pos)
            {
                break;
            }

            pos = (int)next;
        }

        return types;
    }

    private static void ProcessSegments(byte[] d, Jbig2Bitmap page, Dictionary<long, List<Jbig2Bitmap>> symbolDicts)
    {
        int pos = 0;
        while (pos + 11 <= d.Length)
        {
            if (!TryParseHeader(d, pos, out Segment seg))
            {
                break;
            }

            long dataLength = seg.DataLength;
            long next = seg.DataStart + dataLength;
            if (dataLength < 0 || next > d.Length)
            {
                next = d.Length;
                dataLength = d.Length - seg.DataStart;
            }

            try
            {
                switch (seg.Type)
                {
                    case PageInfo:
                        ApplyPageInfo(d, seg.DataStart, page);
                        break;

                    case SymbolDictionary:
                        symbolDicts[seg.Number] = Jbig2SymbolDict.Decode(
                            d, seg.DataStart, (int)dataLength, Gather(seg.ReferredTo, symbolDicts));
                        break;

                    case IntermediateTextRegion:
                    case ImmediateTextRegion:
                    case ImmediateLosslessTextRegion:
                    {
                        Jbig2Region? region = Jbig2TextRegion.Decode(
                            d, seg.DataStart, (int)dataLength, Gather(seg.ReferredTo, symbolDicts));
                        if (region is { } r)
                        {
                            page.Combine(r.Bitmap, r.X, r.Y, r.CombOp);
                        }

                        break;
                    }

                    case IntermediateGenericRegion:
                    case ImmediateGenericRegion:
                    case ImmediateLosslessGenericRegion:
                        DecodeGenericRegion(d, seg.DataStart, (int)dataLength, page);
                        break;

                    default:
                        break; // halftone / refinement / control — skipped
                }
            }
            catch
            {
                // A malformed segment is isolated; continue with the rest of the page.
            }

            pos = (int)next;
        }
    }

    private static List<Jbig2Bitmap> Gather(long[] referredTo, Dictionary<long, List<Jbig2Bitmap>> symbolDicts)
    {
        var symbols = new List<Jbig2Bitmap>();
        foreach (long r in referredTo)
        {
            if (symbolDicts.TryGetValue(r, out List<Jbig2Bitmap>? dict))
            {
                symbols.AddRange(dict);
            }
        }

        return symbols;
    }

    private static bool TryParseHeader(byte[] d, int pos, out Segment seg)
    {
        seg = default;

        long segNum = U32(d, pos);
        int flags = d[pos + 4];
        int type = flags & 0x3F;
        int pageAssocSize = (flags & 0x40) != 0 ? 4 : 1;

        int p = pos + 5;
        if (p >= d.Length)
        {
            return false;
        }

        int rtByte = d[p];
        long refCount = (uint)rtByte >> 5;
        if (refCount == 7)
        {
            refCount = U32(d, p) & 0x1FFFFFFF;
            p += 4;
            p += (int)((refCount + 8) / 8); // retention-flag bytes
        }
        else
        {
            p += 1;
        }

        if (refCount < 0 || refCount > 1_000_000)
        {
            return false;
        }

        int refSize = segNum <= 256 ? 1 : (segNum <= 65536 ? 2 : 4);
        var referredTo = new long[refCount];
        for (int i = 0; i < refCount; i++)
        {
            if (p + refSize > d.Length)
            {
                return false;
            }

            referredTo[i] = ReadNum(d, p, refSize);
            p += refSize;
        }

        p += pageAssocSize;
        if (p + 4 > d.Length)
        {
            return false;
        }

        long dataLength = U32(d, p);
        p += 4;

        seg = new Segment
        {
            Number = segNum,
            Type = type,
            ReferredTo = referredTo,
            DataStart = p,
            DataLength = dataLength,
        };
        return true;
    }

    private static void ApplyPageInfo(byte[] d, int p, Jbig2Bitmap page)
    {
        if (p + 17 > d.Length)
        {
            return;
        }

        if ((d[p + 16] & 0x04) != 0)
        {
            for (int i = 0; i < page.Data.Length; i++)
            {
                page.Data[i] = 1;
            }
        }
    }

    private static void DecodeGenericRegion(byte[] d, int segStart, int segLen, Jbig2Bitmap page)
    {
        int p = segStart;
        int rw = (int)U32(d, p);
        int rh = (int)U32(d, p + 4);
        int rx = (int)U32(d, p + 8);
        int ry = (int)U32(d, p + 12);
        int combOp = d[p + 16] & 7;

        int gflags = d[p + 17];
        bool mmr = (gflags & 1) != 0;
        int template = (gflags >> 1) & 3;
        bool tpgdon = (gflags & 8) != 0;

        int q = p + 18;
        var at = Array.Empty<(int X, int Y)>();
        if (!mmr)
        {
            int nat = template == 0 ? 4 : 1;
            at = new (int X, int Y)[nat];
            for (int i = 0; i < nat; i++)
            {
                at[i] = ((sbyte)d[q], (sbyte)d[q + 1]);
                q += 2;
            }
        }

        Jbig2Bitmap region;
        if (mmr)
        {
            int len = (segStart + segLen) - q;
            var sub = new byte[len];
            Array.Copy(d, q, sub, 0, len);
            RasterImage raster = CcittFaxDecode.Decode(sub, new CcittParams { K = -1, Columns = rw, Rows = rh });
            region = FromRaster(raster);
        }
        else
        {
            var mq = new MqDecoder(d, q, segStart + segLen);
            var cx = new ArithContext(1 << 16);
            region = Jbig2GenericRegion.Decode(mq, cx, rw, rh, template, at, tpgdon);
        }

        page.Combine(region, rx, ry, combOp);
    }

    private static Jbig2Bitmap FromRaster(RasterImage raster)
    {
        var bmp = new Jbig2Bitmap(raster.Width, raster.Height);
        for (int i = 0; i < bmp.Data.Length && i < raster.Samples.Length; i++)
        {
            bmp.Data[i] = (byte)(raster.Samples[i] == 0 ? 1 : 0);
        }

        return bmp;
    }

    private static RasterImage ToRaster(Jbig2Bitmap page)
    {
        var samples = new byte[page.Data.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = page.Data[i] == 1 ? (byte)0 : (byte)255;
        }

        return new RasterImage
        {
            Width = page.Width,
            Height = page.Height,
            Components = 1,
            Samples = samples,
        };
    }

    private static long ReadNum(byte[] d, int o, int size)
    {
        long v = 0;
        for (int i = 0; i < size; i++)
        {
            v = (v << 8) | d[o + i];
        }

        return v;
    }

    private static long U32(byte[] d, int o)
        => o + 4 <= d.Length
            ? ((long)d[o] << 24) | ((long)d[o + 1] << 16) | ((long)d[o + 2] << 8) | d[o + 3]
            : 0;

    private struct Segment
    {
        public long Number;
        public int Type;
        public long[] ReferredTo;
        public int DataStart;
        public long DataLength;
    }
}

/// <summary>Which JBIG2 features a stream uses (from <see cref="Jbig2Decode.Inspect"/>).</summary>
public struct Jbig2Capabilities
{
    /// <summary>Uses Huffman-coded symbol dictionaries or text regions (not yet supported).</summary>
    public bool UsesHuffman { get; set; }

    /// <summary>Uses refinement (region refinement or refinement-aggregate coding) (not yet supported).</summary>
    public bool UsesRefinement { get; set; }

    /// <summary>Uses halftone / pattern-dictionary regions (not yet supported).</summary>
    public bool UsesHalftone { get; set; }

    /// <summary>Contains at least one text region.</summary>
    public bool HasText { get; set; }

    /// <summary>Contains at least one generic region.</summary>
    public bool HasGeneric { get; set; }

    /// <summary>True when the stream uses only features this decoder fully supports.</summary>
    public readonly bool Supported => !UsesHuffman && !UsesRefinement && !UsesHalftone;
}
