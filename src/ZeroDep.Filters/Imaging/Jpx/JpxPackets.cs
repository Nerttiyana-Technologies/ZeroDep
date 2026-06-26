using System;
using System.Collections.Generic;

namespace ZeroDep.Filters.Jpx;

/// <summary>
/// Tier-2 of the JPEG 2000 decoder (ITU-T T.800 §B): builds tile-component geometry (resolutions,
/// sub-bands, precincts, code-blocks) and parses packet headers in progression order (LRCP and RLCP),
/// collecting each code-block's compressed byte ranges. Only the bit-stream layout is handled here; the
/// arithmetic coefficient decode is Tier-1 (<see cref="JpxTier1"/>).
/// </summary>
internal static class JpxPackets
{
    private static readonly (int Xob, int Yob)[] DetailBands =
    {
        (1, 0), // HL
        (0, 1), // LH
        (1, 1), // HH
    };

    /// <summary>Builds geometry for every component of a tile, given the tile's clipped sample rectangle.</summary>
    public static JpxTileComponent[] BuildTile(JpxImage image, int tileIndex, int tx0, int ty0, int tx1, int ty1)
    {
        JpxComponent[] comps = image.Siz.Components;
        var result = new JpxTileComponent[comps.Length];

        for (int c = 0; c < comps.Length; c++)
        {
            JpxComponent sc = comps[c];
            JpxCod cod = image.CodFor(tileIndex, c);
            JpxQcd qcd = image.QcdFor(tileIndex, c);

            int tcx0 = (int)CeilDiv(tx0, sc.XRsiz);
            int tcy0 = (int)CeilDiv(ty0, sc.YRsiz);
            int tcx1 = (int)CeilDiv(tx1, sc.XRsiz);
            int tcy1 = (int)CeilDiv(ty1, sc.YRsiz);

            var tc = new JpxTileComponent
            {
                Component = c,
                X0 = tcx0,
                Y0 = tcy0,
                X1 = tcx1,
                Y1 = tcy1,
                Cod = cod,
                Qcd = qcd,
                Siz = sc,
            };

            int nl = cod.DecompositionLevels;
            var resolutions = new JpxResolution[nl + 1];
            for (int r = 0; r <= nl; r++)
            {
                resolutions[r] = BuildResolution(tc, r, nl, cod, qcd);
            }

            tc.Resolutions = resolutions;
            result[c] = tc;
        }

        return result;
    }

    private static JpxResolution BuildResolution(JpxTileComponent tc, int r, int nl, JpxCod cod, JpxQcd qcd)
    {
        int shift = nl - r;
        int trx0 = (int)CeilDiv(tc.X0, 1L << shift);
        int try0 = (int)CeilDiv(tc.Y0, 1L << shift);
        int trx1 = (int)CeilDiv(tc.X1, 1L << shift);
        int try1 = (int)CeilDiv(tc.Y1, 1L << shift);

        (int ppx, int ppy) = PrecinctExp(cod, r);
        int numPrecW = trx1 > trx0 ? CeilShift(trx1, ppx) - (trx0 >> ppx) : 0;
        int numPrecH = try1 > try0 ? CeilShift(try1, ppy) - (try0 >> ppy) : 0;

        var res = new JpxResolution
        {
            ResLevel = r,
            X0 = trx0,
            Y0 = try0,
            X1 = trx1,
            Y1 = try1,
            NumPrecinctsWide = numPrecW,
            NumPrecinctsHigh = numPrecH,
        };

        // Code-block exponents are clamped to the precinct (which is halved for detail resolutions).
        int xcb = Math.Min(cod.CodeBlockWidthExp, r == 0 ? ppx : ppx - 1);
        int ycb = Math.Min(cod.CodeBlockHeightExp, r == 0 ? ppy : ppy - 1);
        xcb = Math.Max(xcb, 0);
        ycb = Math.Max(ycb, 0);

        JpxSubband[] subbands;
        if (r == 0)
        {
            subbands = new[] { BuildSubband(tc, res, 0, nl, 0, 0, 0, xcb, ycb, qcd, numPrecW, numPrecH, ppx, ppy) };
        }
        else
        {
            int nb = nl - r + 1; // decomposition level of this resolution's detail bands
            subbands = new JpxSubband[3];
            for (int b = 0; b < 3; b++)
            {
                int orient = b + 1; // 1=HL, 2=LH, 3=HH
                subbands[b] = BuildSubband(
                    tc, res, orient, nl, nb, DetailBands[b].Xob, DetailBands[b].Yob, xcb, ycb, qcd, numPrecW, numPrecH, ppx, ppy);
            }
        }

        res.Subbands = subbands;
        return res;
    }

    private static JpxSubband BuildSubband(
        JpxTileComponent tc, JpxResolution res, int orient, int nl, int nb, int xob, int yob,
        int xcb, int ycb, JpxQcd qcd, int numPrecW, int numPrecH, int ppx, int ppy)
    {
        int tbx0;
        int tby0;
        int tbx1;
        int tby1;
        if (orient == 0)
        {
            tbx0 = res.X0;
            tby0 = res.Y0;
            tbx1 = res.X1;
            tby1 = res.Y1;
        }
        else
        {
            long half = 1L << (nb - 1);
            long full = 1L << nb;
            tbx0 = (int)CeilDiv(tc.X0 - (half * xob), full);
            tby0 = (int)CeilDiv(tc.Y0 - (half * yob), full);
            tbx1 = (int)CeilDiv(tc.X1 - (half * xob), full);
            tby1 = (int)CeilDiv(tc.Y1 - (half * yob), full);
        }

        int stepIndex = orient == 0 ? 0 : (1 + ((nl - nb) * 3) + (orient - 1));
        (int exp, int mant) = StepFor(qcd, stepIndex, orient, nl, nb);

        var band = new JpxSubband
        {
            Orientation = orient,
            X0 = tbx0,
            Y0 = tby0,
            X1 = tbx1,
            Y1 = tby1,
            Exponent = exp,
            Mantissa = mant,
            NumBps = Math.Max(0, qcd.GuardBits + exp - 1),
        };

        BuildCodeBlocks(band, xcb, ycb, numPrecW, numPrecH, ppx, ppy, res);
        return band;
    }

    private static void BuildCodeBlocks(
        JpxSubband band, int xcb, int ycb, int numPrecW, int numPrecH, int ppx, int ppy, JpxResolution res)
    {
        int numPrec = Math.Max(1, numPrecW) * Math.Max(1, numPrecH);
        int cbw = 1 << xcb;
        int cbh = 1 << ycb;

        int cbx0 = band.X0 >> xcb;
        int cby0 = band.Y0 >> ycb;
        int cbx1 = (band.X1 + cbw - 1) >> xcb;
        int cby1 = (band.Y1 + cbh - 1) >> ycb;

        // Precinct exponents in sub-band coordinates (halved for detail resolutions).
        int ppxBand = Math.Max(0, res.ResLevel == 0 ? ppx : ppx - 1);
        int ppyBand = Math.Max(0, res.ResLevel == 0 ? ppy : ppy - 1);
        int precOriginX = band.X0 >> ppxBand;
        int precOriginY = band.Y0 >> ppyBand;

        var blocks = new List<JpxCodeBlock>();
        var perPrecinct = new List<int>[numPrec];
        for (int i = 0; i < numPrec; i++)
        {
            perPrecinct[i] = new List<int>();
        }

        for (int gy = cby0; gy < cby1; gy++)
        {
            for (int gx = cbx0; gx < cbx1; gx++)
            {
                int bx0 = Math.Max(band.X0, gx * cbw);
                int by0 = Math.Max(band.Y0, gy * cbh);
                int bx1 = Math.Min(band.X1, (gx + 1) * cbw);
                int by1 = Math.Min(band.Y1, (gy + 1) * cbh);
                if (bx1 <= bx0 || by1 <= by0)
                {
                    continue;
                }

                int px = ((gx * cbw) >> ppxBand) - precOriginX;
                int py = ((gy * cbh) >> ppyBand) - precOriginY;
                int wide = Math.Max(1, numPrecW);
                int precinct = (py * wide) + px;
                if (precinct < 0 || precinct >= numPrec)
                {
                    precinct = 0;
                }

                var cb = new JpxCodeBlock
                {
                    X0 = bx0,
                    Y0 = by0,
                    X1 = bx1,
                    Y1 = by1,
                    GridX = gx,
                    GridY = gy,
                };

                int index = blocks.Count;
                blocks.Add(cb);
                perPrecinct[precinct].Add(index);
            }
        }

        band.CodeBlocks = blocks.ToArray();
        band.PrecinctCodeBlocks = perPrecinct;

        // Per-precinct tag trees, sized to that precinct's code-block extent, with local leaf coordinates.
        band.InclusionTrees = new JpxTagTree[numPrec];
        band.ZeroBitTrees = new JpxTagTree[numPrec];
        for (int p = 0; p < numPrec; p++)
        {
            List<int> list = perPrecinct[p];
            if (list.Count == 0)
            {
                band.InclusionTrees[p] = new JpxTagTree(1, 1);
                band.ZeroBitTrees[p] = new JpxTagTree(1, 1);
                continue;
            }

            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;
            foreach (int idx in list)
            {
                JpxCodeBlock cb = band.CodeBlocks[idx];
                minX = Math.Min(minX, cb.GridX);
                minY = Math.Min(minY, cb.GridY);
                maxX = Math.Max(maxX, cb.GridX);
                maxY = Math.Max(maxY, cb.GridY);
            }

            foreach (int idx in list)
            {
                JpxCodeBlock cb = band.CodeBlocks[idx];
                cb.LocalX = cb.GridX - minX;
                cb.LocalY = cb.GridY - minY;
            }

            band.InclusionTrees[p] = new JpxTagTree(maxX - minX + 1, maxY - minY + 1);
            band.ZeroBitTrees[p] = new JpxTagTree(maxX - minX + 1, maxY - minY + 1);
        }
    }

    /// <summary>
    /// Reads all packets of a tile (every layer/resolution/component/precinct in the progression order),
    /// filling each code-block's <see cref="JpxCodeBlock.Segments"/>. Supports LRCP and RLCP.
    /// </summary>
    public static void ReadPackets(JpxImage image, JpxTileComponent[] tile, byte[] data, int start, int end)
    {
        JpxCod cod = tile.Length > 0 ? tile[0].Cod : image.Cod;
        int layers = Math.Max(1, cod.Layers);
        int maxRes = 0;
        foreach (JpxTileComponent tc in tile)
        {
            maxRes = Math.Max(maxRes, tc.Resolutions.Length);
        }

        int pos = start;

        if (cod.Progression == 1)
        {
            // RLCP
            for (int r = 0; r < maxRes; r++)
            {
                for (int l = 0; l < layers; l++)
                {
                    for (int c = 0; c < tile.Length; c++)
                    {
                        pos = ReadComponentResolutionLayer(cod, tile[c], r, l, data, pos, end);
                    }
                }
            }
        }
        else
        {
            // LRCP (default)
            for (int l = 0; l < layers; l++)
            {
                for (int r = 0; r < maxRes; r++)
                {
                    for (int c = 0; c < tile.Length; c++)
                    {
                        pos = ReadComponentResolutionLayer(cod, tile[c], r, l, data, pos, end);
                    }
                }
            }
        }
    }

    private static int ReadComponentResolutionLayer(
        JpxCod cod, JpxTileComponent tc, int r, int layer, byte[] data, int pos, int end)
    {
        if (r >= tc.Resolutions.Length)
        {
            return pos;
        }

        JpxResolution res = tc.Resolutions[r];
        int numPrec = Math.Max(1, res.NumPrecinctsWide) * Math.Max(1, res.NumPrecinctsHigh);
        for (int p = 0; p < numPrec; p++)
        {
            pos = ReadPacket(cod, res, p, layer, data, pos, end);
            if (pos >= end)
            {
                break;
            }
        }

        return pos;
    }

    private static int ReadPacket(JpxCod cod, JpxResolution res, int precinct, int layer, byte[] data, int pos, int end)
    {
        // Optional SOP marker before the packet.
        if (cod.UseSop && pos + 1 < end && data[pos] == 0xFF && data[pos + 1] == 0x91)
        {
            pos += 6;
        }

        var reader = new JpxBitReader(data, pos, end);
        bool nonEmpty = reader.ReadBit() == 1;

        if (nonEmpty)
        {
            foreach (JpxSubband band in res.Subbands)
            {
                if (precinct >= band.PrecinctCodeBlocks.Length)
                {
                    continue;
                }

                List<int> list = band.PrecinctCodeBlocks[precinct];
                JpxTagTree inclTree = band.InclusionTrees[precinct];
                JpxTagTree zbpTree = band.ZeroBitTrees[precinct];

                foreach (int idx in list)
                {
                    JpxCodeBlock cb = band.CodeBlocks[idx];
                    cb.PendingPasses = 0;
                    cb.PendingLength = 0;

                    bool included;
                    if (cb.Included)
                    {
                        included = reader.ReadBit() == 1;
                    }
                    else
                    {
                        int v = inclTree.Decode(reader, cb.LocalX, cb.LocalY, layer + 1);
                        included = v <= layer;
                    }

                    if (!included)
                    {
                        continue;
                    }

                    if (!cb.Included)
                    {
                        cb.Included = true;
                        cb.ZeroBitPlanes = zbpTree.Decode(reader, cb.LocalX, cb.LocalY, 1 << 20);
                    }

                    int passes = ReadCodingPasses(reader);
                    while (reader.ReadBit() == 1)
                    {
                        cb.LBlock++;
                    }

                    int lengthBits = cb.LBlock + IntLog2(passes);
                    int length = reader.ReadBits(lengthBits);
                    cb.PendingPasses = passes;
                    cb.PendingLength = length;
                }
            }
        }

        reader.ByteAlign();
        int body = reader.Position;

        // Optional EPH marker terminates the packet header.
        if (cod.UseEph && body + 1 < end && data[body] == 0xFF && data[body + 1] == 0x92)
        {
            body += 2;
        }

        int bodyPos = body;
        if (nonEmpty)
        {
            foreach (JpxSubband band in res.Subbands)
            {
                if (precinct >= band.PrecinctCodeBlocks.Length)
                {
                    continue;
                }

                foreach (int idx in band.PrecinctCodeBlocks[precinct])
                {
                    JpxCodeBlock cb = band.CodeBlocks[idx];
                    if (cb.PendingPasses <= 0)
                    {
                        continue;
                    }

                    int len = cb.PendingLength;
                    if (bodyPos + len > end)
                    {
                        len = Math.Max(0, end - bodyPos);
                    }

                    cb.Segments.Add((bodyPos, len));
                    cb.NumPasses += cb.PendingPasses;
                    bodyPos += len;
                }
            }
        }

        return bodyPos;
    }

    // T.800 Table B.4 — number of coding passes signalled in a packet header.
    private static int ReadCodingPasses(JpxBitReader reader)
    {
        if (reader.ReadBit() == 0)
        {
            return 1;
        }

        if (reader.ReadBit() == 0)
        {
            return 2;
        }

        int v = reader.ReadBits(2);
        if (v != 3)
        {
            return 3 + v;
        }

        v = reader.ReadBits(5);
        if (v != 31)
        {
            return 6 + v;
        }

        v = reader.ReadBits(7);
        return 37 + v;
    }

    private static (int Exp, int Mant) StepFor(JpxQcd qcd, int stepIndex, int orient, int nl, int nb)
    {
        (int Exponent, int Mantissa)[] steps = qcd.StepSizes;
        if (steps.Length == 0)
        {
            return (8, 0);
        }

        if (qcd.Style == 1 && steps.Length == 1)
        {
            // Scalar derived: only the LL step is given; the rest follow from the decomposition level.
            int baseExp = steps[0].Exponent;
            int baseMant = steps[0].Mantissa;
            int level = orient == 0 ? nl : nb;
            int exp = baseExp - nl + level;
            return (Math.Max(0, exp), baseMant);
        }

        if (stepIndex < steps.Length)
        {
            return steps[stepIndex];
        }

        return steps[steps.Length - 1];
    }

    private static (int Ppx, int Ppy) PrecinctExp(JpxCod cod, int r)
    {
        if (cod.PrecinctSizes is { } sizes && r < sizes.Length)
        {
            int x = sizes[r].X == 0 ? 15 : sizes[r].X;
            int y = sizes[r].Y == 0 ? 15 : sizes[r].Y;
            return (x, y);
        }

        return (15, 15);
    }

    private static int IntLog2(int n)
    {
        int v = 0;
        while (n > 1)
        {
            n >>= 1;
            v++;
        }

        return v;
    }

    private static int CeilShift(int value, int shift) => (value + (1 << shift) - 1) >> shift;

    private static long CeilDiv(long a, long b) => a >= 0 ? (a + b - 1) / b : -((-a) / b);
}
