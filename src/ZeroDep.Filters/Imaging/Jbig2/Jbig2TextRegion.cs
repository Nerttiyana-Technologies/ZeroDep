using System.Collections.Generic;

namespace ZeroDep.Filters.Jbig2;

/// <summary>The decoded result of a region segment: a bitmap and where/how to composite it on the page.</summary>
internal readonly struct Jbig2Region
{
    public Jbig2Region(Jbig2Bitmap bitmap, int x, int y, int combOp)
    {
        Bitmap = bitmap;
        X = x;
        Y = y;
        CombOp = combOp;
    }

    public Jbig2Bitmap Bitmap { get; }

    public int X { get; }

    public int Y { get; }

    public int CombOp { get; }
}

/// <summary>
/// Decodes a JBIG2 text region (ITU-T T.88 §6.4), arithmetic variant (SBHUFF = 0). Places instances of
/// previously-decoded symbols onto the region bitmap by decoded (S, T) coordinates and symbol IDs.
/// Inline refinement (SBREFINE) is decoded but the base symbol is drawn (refinement does not appear in
/// real PDFs). Returns null for the unsupported Huffman variant.
/// </summary>
internal static class Jbig2TextRegion
{
    public static Jbig2Region? Decode(byte[] d, int dataStart, int dataLen, IReadOnlyList<Jbig2Bitmap> symbols)
    {
        int p = dataStart;
        int regW = (int)U32(d, p);
        int regH = (int)U32(d, p + 4);
        int regX = (int)U32(d, p + 8);
        int regY = (int)U32(d, p + 12);
        int regCombOp = d[p + 16] & 7;
        p += 17;

        int flags = (d[p] << 8) | d[p + 1];
        p += 2;

        bool sbhuff = (flags & 1) != 0;
        if (sbhuff)
        {
            return null; // Huffman text regions unsupported
        }

        bool sbrefine = (flags & 2) != 0;
        int logStrips = (flags >> 2) & 3;
        int refCorner = (flags >> 4) & 3;
        bool transposed = ((flags >> 6) & 1) != 0;
        int sbCombOp = (flags >> 7) & 3;
        int sbDefPixel = (flags >> 9) & 1;
        int sbDsOffset = (flags >> 10) & 0x1F;
        if (sbDsOffset > 15)
        {
            sbDsOffset -= 32;
        }

        int sbrTemplate = (flags >> 15) & 1;
        if (sbrefine && sbrTemplate == 0)
        {
            p += 4; // SBRAT
        }

        int numInstances = (int)U32(d, p);
        p += 4;

        int sbStrips = 1 << logStrips;
        int numSyms = symbols.Count;
        int symCodeLen = CeilLog2(numSyms);
        if (symCodeLen < 1)
        {
            symCodeLen = 1;
        }

        var mq = new MqDecoder(d, p, dataStart + dataLen);
        var iadt = new ArithContext(512);
        var iafs = new ArithContext(512);
        var iads = new ArithContext(512);
        var iait = new ArithContext(512);
        var iari = new ArithContext(512);
        var iaid = new ArithContext(1 << (symCodeLen + 1));

        var region = new Jbig2Bitmap(regW, regH, (byte)sbDefPixel);

        int stripT = -Jbig2ArithInt.DecodeInt(mq, iadt) * sbStrips;
        int firstS = 0;
        int instances = 0;
        int guard = 0;
        int maxGuard = numInstances + 1024;

        while (instances < numInstances && guard++ < maxGuard)
        {
            int dt = Jbig2ArithInt.DecodeInt(mq, iadt);
            if (dt == Jbig2ArithInt.Oob)
            {
                break;
            }

            stripT += dt * sbStrips;

            int dfs = Jbig2ArithInt.DecodeInt(mq, iafs);
            if (dfs == Jbig2ArithInt.Oob)
            {
                break;
            }

            firstS += dfs;
            int curS = firstS;
            bool first = true;

            while (instances < numInstances)
            {
                if (!first)
                {
                    int ids = Jbig2ArithInt.DecodeInt(mq, iads);
                    if (ids == Jbig2ArithInt.Oob)
                    {
                        break; // end of strip
                    }

                    curS += ids + sbDsOffset;
                }

                int curT = sbStrips == 1 ? 0 : Jbig2ArithInt.DecodeInt(mq, iait);
                if (curT == Jbig2ArithInt.Oob)
                {
                    break;
                }

                int t = stripT + curT;
                int id = Jbig2ArithInt.DecodeIaid(mq, iaid, symCodeLen);

                if (sbrefine)
                {
                    Jbig2ArithInt.DecodeInt(mq, iari); // RI flag; base symbol drawn regardless
                }

                Jbig2Bitmap? sym = id >= 0 && id < numSyms ? symbols[id] : null;
                if (sym is not null)
                {
                    Place(region, sym, ref curS, t, refCorner, transposed, sbCombOp);
                }

                instances++;
                first = false;
            }
        }

        return new Jbig2Region(region, regX, regY, regCombOp);
    }

    private static void Place(Jbig2Bitmap region, Jbig2Bitmap sym, ref int curS, int t, int refCorner, bool transposed, int op)
    {
        int w = sym.Width;
        int h = sym.Height;
        bool right = refCorner == 2 || refCorner == 3; // BOTTOMRIGHT / TOPRIGHT
        bool top = refCorner == 1 || refCorner == 3;   // TOPLEFT / TOPRIGHT

        if (!transposed)
        {
            if (right)
            {
                curS += w - 1;
            }

            int x = right ? curS - w + 1 : curS;
            int y = top ? t : t - h + 1;
            region.Combine(sym, x, y, op);

            if (!right)
            {
                curS += w - 1;
            }
        }
        else
        {
            bool bottom = refCorner == 0 || refCorner == 2;
            if (bottom)
            {
                curS += h - 1;
            }

            int y = bottom ? curS - h + 1 : curS;
            int x = right ? t - w + 1 : t;
            region.Combine(sym, x, y, op);

            if (!bottom)
            {
                curS += h - 1;
            }
        }
    }

    private static int CeilLog2(int n)
    {
        int l = 0;
        while ((1 << l) < n)
        {
            l++;
        }

        return l;
    }

    private static long U32(byte[] d, int o)
        => ((long)d[o] << 24) | ((long)d[o + 1] << 16) | ((long)d[o + 2] << 8) | d[o + 3];
}
