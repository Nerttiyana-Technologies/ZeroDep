using System;
using System.Collections.Generic;

namespace ZeroDep.Filters;

/// <summary>
/// Progressive JPEG (SOF2) decoding: multiple scans refine DC and AC coefficients by spectral
/// selection and successive approximation (ITU-T T.81 §G). Coefficients accumulate across all scans
/// into per-component buffers, which are then dequantized and inverse-DCT'd to pixels.
/// </summary>
public static partial class JpegDecoder
{
    private static PlaneSet DecodeProgressive(byte[] data, JpegMetadata meta)
    {
        int n = meta.ComponentCount;
        int hMax = 1, vMax = 1;
        foreach (JpegComponent comp in meta.Components)
        {
            hMax = Math.Max(hMax, comp.HorizontalSampling);
            vMax = Math.Max(vMax, comp.VerticalSampling);
        }

        int mcusX = (meta.Width + (8 * hMax) - 1) / (8 * hMax);
        int mcusY = (meta.Height + (8 * vMax) - 1) / (8 * vMax);

        var blocksPerLine = new int[n];
        var blocksPerColumn = new int[n];
        var blocksWide = new int[n];
        var blocksHigh = new int[n];
        var coef = new int[n][];
        for (int c = 0; c < n; c++)
        {
            JpegComponent comp = meta.Components[c];
            blocksPerLine[c] = mcusX * comp.HorizontalSampling;
            blocksPerColumn[c] = mcusY * comp.VerticalSampling;
            int samplesW = ((meta.Width * comp.HorizontalSampling) + hMax - 1) / hMax;
            int samplesH = ((meta.Height * comp.VerticalSampling) + vMax - 1) / vMax;
            blocksWide[c] = (samplesW + 7) / 8;
            blocksHigh[c] = (samplesH + 7) / 8;
            coef[c] = new int[blocksPerLine[c] * blocksPerColumn[c] * 64];
        }

        var huff = new Dictionary<int, HuffTable>();
        var quant = new Dictionary<int, int[]>();
        foreach (KeyValuePair<int, int[]> kv in meta.QuantizationTables)
        {
            quant[kv.Key] = kv.Value;
        }

        int restartInterval = meta.RestartInterval;

        int pos = 2;
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
                pos--;
                continue;
            }

            if (marker == 0xD9)
            {
                break;   // EOI
            }

            if (marker == 0xD8 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0x01)
            {
                continue;
            }

            if (pos + 2 > data.Length)
            {
                break;
            }

            int length = (data[pos] << 8) | data[pos + 1];
            int segStart = pos + 2;
            int segEnd = pos + length;

            if (marker == 0xC4)
            {
                ParseHuffmanInto(data, segStart, segEnd, huff);
            }
            else if (marker == 0xDB)
            {
                ParseQuantInto(data, segStart, segEnd, quant);
            }
            else if (marker == 0xDD)
            {
                if (segEnd - segStart >= 2)
                {
                    restartInterval = (data[segStart] << 8) | data[segStart + 1];
                }
            }
            else if (marker == 0xDA)
            {
                int ns = data[segStart];
                var scanComp = new int[ns];
                var dcId = new int[ns];
                var acId = new int[ns];
                int p = segStart + 1;
                for (int i = 0; i < ns; i++)
                {
                    int selector = data[p];
                    int tables = data[p + 1];
                    p += 2;
                    scanComp[i] = IndexOfComponent(meta, selector);
                    dcId[i] = (tables >> 4) & 0x0F;
                    acId[i] = tables & 0x0F;
                }

                int ss = data[p];
                int se = data[p + 1];
                int ahal = data[p + 2];
                int ah = (ahal >> 4) & 0x0F;
                int al = ahal & 0x0F;

                int entropyStart = segEnd;
                int entropyEnd = NextMarker(data, entropyStart);
                DecodeProgressiveScan(data, entropyStart, meta, huff, coef, blocksPerLine, blocksWide, blocksHigh, mcusX, mcusY, scanComp, dcId, acId, ss, se, ah, al, restartInterval);
                pos = entropyEnd;
                continue;
            }

            pos = segEnd;
        }

        return ReconstructPlanes(meta, quant, coef, blocksPerLine, blocksPerColumn, hMax, vMax);
    }

    private static PlaneSet ReconstructPlanes(JpegMetadata meta, Dictionary<int, int[]> quant, int[][] coef, int[] blocksPerLine, int[] blocksPerColumn, int hMax, int vMax)
    {
        int n = meta.ComponentCount;
        var planeW = new int[n];
        var planes = new byte[n][];
        var block = new byte[64];
        var work = new int[64];

        for (int c = 0; c < n; c++)
        {
            JpegComponent comp = meta.Components[c];
            planeW[c] = blocksPerLine[c] * 8;
            int planeH = blocksPerColumn[c] * 8;
            planes[c] = new byte[planeW[c] * planeH];

            int[] q = quant.TryGetValue(comp.QuantizationTableId, out int[]? qq) ? qq : OnesQuant();
            var quantNatural = new int[64];
            for (int k = 0; k < 64; k++)
            {
                quantNatural[ZigZag[k]] = q[k];
            }

            for (int by = 0; by < blocksPerColumn[c]; by++)
            {
                for (int bx = 0; bx < blocksPerLine[c]; bx++)
                {
                    int bo = ((by * blocksPerLine[c]) + bx) * 64;
                    for (int i = 0; i < 64; i++)
                    {
                        work[i] = coef[c][bo + i] * quantNatural[i];
                    }

                    InverseDct(work, block);
                    PlaceBlock(planes[c], planeW[c], bx * 8, by * 8, block);
                }
            }
        }

        return new PlaneSet { Meta = meta, Planes = planes, PlaneW = planeW, HMax = hMax, VMax = vMax };
    }

    private static void DecodeProgressiveScan(byte[] data, int entropyStart, JpegMetadata meta, Dictionary<int, HuffTable> huff, int[][] coef, int[] blocksPerLine, int[] blocksWide, int[] blocksHigh, int mcusX, int mcusY, int[] scanComp, int[] dcId, int[] acId, int ss, int se, int ah, int al, int restartInterval)
    {
        var reader = new BitReader(data, entropyStart);
        var pred = new int[meta.ComponentCount];
        var eobrun = new int[1];
        bool dcScan = ss == 0;
        int restartCounter = 0;

        if (scanComp.Length == 1)
        {
            int c = scanComp[0];
            int bw = blocksWide[c];
            int bh = blocksHigh[c];
            HuffTable? dc = dcScan && ah == 0 ? huff[(0 << 4) | dcId[0]] : null;
            HuffTable? ac = !dcScan ? huff[(1 << 4) | acId[0]] : null;

            for (int by = 0; by < bh; by++)
            {
                for (int bx = 0; bx < bw; bx++)
                {
                    int bo = ((by * blocksPerLine[c]) + bx) * 64;
                    if (dcScan)
                    {
                        if (ah == 0)
                        {
                            DecodeDcFirst(reader, dc!, coef[c], bo, al, ref pred[c]);
                        }
                        else
                        {
                            DecodeDcRefine(reader, coef[c], bo, al);
                        }
                    }
                    else if (ah == 0)
                    {
                        DecodeAcFirst(reader, ac!, coef[c], bo, ss, se, al, eobrun);
                    }
                    else
                    {
                        DecodeAcRefine(reader, ac!, coef[c], bo, ss, se, al, eobrun);
                    }

                    if (restartInterval > 0 && ++restartCounter == restartInterval && !(by == bh - 1 && bx == bw - 1))
                    {
                        restartCounter = 0;
                        reader.Restart();
                        eobrun[0] = 0;
                        Array.Clear(pred, 0, pred.Length);
                    }
                }
            }

            return;
        }

        // Interleaved scan (DC only).
        for (int my = 0; my < mcusY; my++)
        {
            for (int mx = 0; mx < mcusX; mx++)
            {
                for (int i = 0; i < scanComp.Length; i++)
                {
                    int c = scanComp[i];
                    JpegComponent comp = meta.Components[c];
                    HuffTable dc = huff[(0 << 4) | dcId[i]];
                    for (int by = 0; by < comp.VerticalSampling; by++)
                    {
                        for (int bx = 0; bx < comp.HorizontalSampling; bx++)
                        {
                            int blockRow = (my * comp.VerticalSampling) + by;
                            int blockCol = (mx * comp.HorizontalSampling) + bx;
                            int bo = ((blockRow * blocksPerLine[c]) + blockCol) * 64;
                            if (ah == 0)
                            {
                                DecodeDcFirst(reader, dc, coef[c], bo, al, ref pred[c]);
                            }
                            else
                            {
                                DecodeDcRefine(reader, coef[c], bo, al);
                            }
                        }
                    }
                }

                if (restartInterval > 0 && ++restartCounter == restartInterval && !(my == mcusY - 1 && mx == mcusX - 1))
                {
                    restartCounter = 0;
                    reader.Restart();
                    eobrun[0] = 0;
                    Array.Clear(pred, 0, pred.Length);
                }
            }
        }
    }

    private static void DecodeDcFirst(BitReader r, HuffTable dc, int[] coef, int bo, int al, ref int pred)
    {
        int s = dc.Decode(r);
        int diff = s == 0 ? 0 : Extend(r.Receive(s), s);
        pred += diff;
        coef[bo] = pred << al;
    }

    private static void DecodeDcRefine(BitReader r, int[] coef, int bo, int al)
    {
        if (r.ReadBit() != 0)
        {
            coef[bo] |= 1 << al;
        }
    }

    private static void DecodeAcFirst(BitReader r, HuffTable ac, int[] coef, int bo, int ss, int se, int al, int[] eobrun)
    {
        if (eobrun[0] > 0)
        {
            eobrun[0]--;
            return;
        }

        int k = ss;
        while (k <= se)
        {
            int rs = ac.Decode(r);
            int run = rs >> 4;
            int size = rs & 0x0F;
            if (size == 0)
            {
                if (run < 15)
                {
                    eobrun[0] = 1 << run;
                    if (run > 0)
                    {
                        eobrun[0] += r.Receive(run);
                    }

                    eobrun[0]--;
                    break;
                }

                k += 16;
            }
            else
            {
                k += run;
                if (k > se)
                {
                    break;
                }

                coef[bo + ZigZag[k]] = Extend(r.Receive(size), size) << al;
                k++;
            }
        }
    }

    private static void DecodeAcRefine(BitReader r, HuffTable ac, int[] coef, int bo, int ss, int se, int al, int[] eobrun)
    {
        int p1 = 1 << al;
        int m1 = -1 << al;
        int k = ss;

        if (eobrun[0] == 0)
        {
            for (; k <= se; k++)
            {
                int rs = ac.Decode(r);
                int run = rs >> 4;
                int size = rs & 0x0F;
                int value = 0;

                if (size != 0)
                {
                    value = r.ReadBit() != 0 ? p1 : m1;
                }
                else if (run != 15)
                {
                    eobrun[0] = 1 << run;
                    if (run > 0)
                    {
                        eobrun[0] += r.Receive(run);
                    }

                    break;
                }

                // Skip `run` zero-history coefficients, applying correction bits to nonzero ones.
                for (; k <= se; k++)
                {
                    int idx = bo + ZigZag[k];
                    if (coef[idx] != 0)
                    {
                        if (r.ReadBit() != 0 && (coef[idx] & p1) == 0)
                        {
                            coef[idx] += coef[idx] > 0 ? p1 : m1;
                        }
                    }
                    else
                    {
                        if (run == 0)
                        {
                            break;
                        }

                        run--;
                    }
                }

                if (value != 0 && k <= se)
                {
                    coef[bo + ZigZag[k]] = value;
                }
            }
        }

        if (eobrun[0] > 0)
        {
            for (; k <= se; k++)
            {
                int idx = bo + ZigZag[k];
                if (coef[idx] != 0 && r.ReadBit() != 0 && (coef[idx] & p1) == 0)
                {
                    coef[idx] += coef[idx] > 0 ? p1 : m1;
                }
            }

            eobrun[0]--;
        }
    }

    private static int NextMarker(byte[] data, int start)
    {
        int i = start;
        while (i + 1 < data.Length)
        {
            if (data[i] == 0xFF)
            {
                int m = data[i + 1];
                if (m != 0x00 && !(m >= 0xD0 && m <= 0xD7))
                {
                    return i;
                }
            }

            i++;
        }

        return data.Length;
    }

    private static void ParseHuffmanInto(byte[] d, int s, int e, Dictionary<int, HuffTable> huff)
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
            huff[((tcth >> 4) << 4) | (tcth & 0x0F)] = HuffTable.Build(counts, symbols);
        }
    }

    private static void ParseQuantInto(byte[] d, int s, int e, Dictionary<int, int[]> quant)
    {
        int p = s;
        while (p < e)
        {
            int pqtq = d[p++];
            int sixteenBit = (pqtq >> 4) & 0x0F;
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
}
