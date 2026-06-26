using System;
using System.Collections.Generic;

namespace ZeroDep.Filters.Jpx;

/// <summary>
/// Pure-BCL decoder for PDF <c>/JPXDecode</c> (JPEG 2000) images (ITU-T T.800). Parses the codestream
/// (<see cref="JpxCodestream"/>), reads packets (<see cref="JpxPackets"/>), runs the EBCOT bit-plane
/// coder (<see cref="JpxTier1"/>) reusing the JBIG2 MQ engine, then applies dequantization, the inverse
/// 5/3 or 9/7 wavelet transform (<see cref="JpxWavelet"/>), the inverse multi-component transform, and
/// DC level shifting. Output is a 1-component grayscale or 3-component RGB <see cref="RasterImage"/>.
/// LRCP and RLCP progressions and the default code-block style are supported (the corpus distribution).
/// </summary>
public static class JpxDecode
{
    /// <summary>Decodes a JPEG 2000 codestream (raw or JP2-boxed) to a raster.</summary>
    /// <param name="data">The embedded <c>/JPXDecode</c> bytes.</param>
    /// <param name="width">The PDF <c>/Width</c> (used only as a fallback if the codestream omits SIZ).</param>
    /// <param name="height">The PDF <c>/Height</c> (fallback only).</param>
    public static RasterImage Decode(byte[] data, int width, int height)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        JpxImage image = JpxCodestream.Parse(data);
        JpxSiz siz = image.Siz;
        int comps = siz.Components.Length;
        int w = siz.Width > 0 ? siz.Width : width;
        int h = siz.Height > 0 ? siz.Height : height;
        if (comps == 0 || w <= 0 || h <= 0)
        {
            return Blank(width, height);
        }

        int outComps = comps >= 3 ? 3 : 1;
        var channels = new byte[outComps][];
        for (int i = 0; i < outComps; i++)
        {
            channels[i] = new byte[w * h];
        }

        // Group tile-parts by tile index and decode each tile into the channel canvases.
        var byTile = new Dictionary<int, List<JpxTilePart>>();
        foreach (JpxTilePart tp in image.TileParts)
        {
            if (!byTile.TryGetValue(tp.TileIndex, out List<JpxTilePart>? list))
            {
                list = new List<JpxTilePart>();
                byTile[tp.TileIndex] = list;
            }

            list.Add(tp);
        }

        if (byTile.Count == 0)
        {
            return Blank(w, h);
        }

        foreach (KeyValuePair<int, List<JpxTilePart>> kv in byTile)
        {
            try
            {
                DecodeTile(image, siz, kv.Key, kv.Value, channels, w, h);
            }
            catch
            {
                // A malformed tile is isolated; the rest of the image still decodes.
            }
        }

        return Interleave(channels, w, h, outComps);
    }

    private static void DecodeTile(
        JpxImage image, JpxSiz siz, int tileIndex, List<JpxTilePart> parts, byte[][] channels, int imgW, int imgH)
    {
        int tilesX = Math.Max(1, siz.TilesX);
        int p = tileIndex % tilesX;
        int q = tileIndex / tilesX;

        long xtsiz = siz.XTsiz > 0 ? siz.XTsiz : siz.Xsiz;
        long ytsiz = siz.YTsiz > 0 ? siz.YTsiz : siz.Ysiz;
        int tx0 = (int)Math.Max((long)siz.XTOsiz + ((long)p * xtsiz), siz.XOsiz);
        int ty0 = (int)Math.Max((long)siz.YTOsiz + ((long)q * ytsiz), siz.YOsiz);
        int tx1 = (int)Math.Min((long)siz.XTOsiz + ((long)(p + 1) * xtsiz), siz.Xsiz);
        int ty1 = (int)Math.Min((long)siz.YTOsiz + ((long)(q + 1) * ytsiz), siz.Ysiz);
        if (tx1 <= tx0 || ty1 <= ty0)
        {
            return;
        }

        // Concatenate the tile-part bodies into one contiguous bitstream.
        int total = 0;
        foreach (JpxTilePart tp in parts)
        {
            total += tp.DataLength;
        }

        var tileData = new byte[total];
        int pos = 0;
        foreach (JpxTilePart tp in parts)
        {
            if (tp.DataLength > 0 && tp.DataStart >= 0 && tp.DataStart + tp.DataLength <= image.Data.Length)
            {
                Array.Copy(image.Data, tp.DataStart, tileData, pos, tp.DataLength);
            }

            pos += tp.DataLength;
        }

        JpxTileComponent[] tc = JpxPackets.BuildTile(image, tileIndex, tx0, ty0, tx1, ty1);
        JpxPackets.ReadPackets(image, tc, tileData, 0, total);

        int comps = tc.Length;
        bool reversible = comps > 0 && tc[0].Cod.Reversible;
        bool useMct = comps >= 3 && tc[0].Cod.UseMct;

        if (reversible)
        {
            var planes = new int[comps][];
            for (int c = 0; c < comps; c++)
            {
                planes[c] = ReconstructReversible(tc[c], tileData);
            }

            if (useMct)
            {
                InverseRct(planes);
            }

            WriteReversible(planes, tc, channels, imgW, imgH);
        }
        else
        {
            var planes = new double[comps][];
            for (int c = 0; c < comps; c++)
            {
                planes[c] = ReconstructIrreversible(tc[c], tileData);
            }

            if (useMct)
            {
                InverseIct(planes);
            }

            WriteIrreversible(planes, tc, channels, imgW, imgH);
        }
    }

    private static int[] ReconstructReversible(JpxTileComponent tc, byte[] tileData)
    {
        var data = new int[tc.Resolutions.Length][];
        for (int r = 0; r < tc.Resolutions.Length; r++)
        {
            JpxResolution res = tc.Resolutions[r];
            data[r] = new int[Math.Max(1, res.Width * res.Height)];
        }

        for (int r = 0; r < tc.Resolutions.Length; r++)
        {
            JpxResolution res = tc.Resolutions[r];
            int px = res.X0 & 1;
            int py = res.Y0 & 1;
            foreach (JpxSubband band in res.Subbands)
            {
                foreach (JpxCodeBlock cb in band.CodeBlocks)
                {
                    if (cb.NumPasses <= 0)
                    {
                        continue;
                    }

                    JpxTier1.Result t1 = JpxTier1.Decode(cb, band, tileData);
                    int bw = cb.Width;
                    for (int sy = 0; sy < cb.Height; sy++)
                    {
                        for (int sx = 0; sx < bw; sx++)
                        {
                            int m = t1.Magnitude[(sy * bw) + sx];
                            if (m == 0)
                            {
                                continue;
                            }

                            int coeff = t1.Negative[(sy * bw) + sx] ? -m : m;
                            int lbx = (cb.X0 + sx) - band.X0;
                            int lby = (cb.Y0 + sy) - band.Y0;
                            int idx = PlaceIndex(res, band.Orientation, lbx, lby, px, py);
                            if ((uint)idx < (uint)data[r].Length)
                            {
                                data[r][idx] = coeff;
                            }
                        }
                    }
                }
            }
        }

        return JpxWavelet.SynthesizeReversible(tc.Resolutions, data);
    }

    private static double[] ReconstructIrreversible(JpxTileComponent tc, byte[] tileData)
    {
        var data = new double[tc.Resolutions.Length][];
        for (int r = 0; r < tc.Resolutions.Length; r++)
        {
            JpxResolution res = tc.Resolutions[r];
            data[r] = new double[Math.Max(1, res.Width * res.Height)];
        }

        int depth = tc.Siz.Depth;
        for (int r = 0; r < tc.Resolutions.Length; r++)
        {
            JpxResolution res = tc.Resolutions[r];
            int px = res.X0 & 1;
            int py = res.Y0 & 1;
            foreach (JpxSubband band in res.Subbands)
            {
                int gain = GainLog2(band.Orientation);
                double delta = (1.0 + (band.Mantissa / 2048.0)) * Math.Pow(2.0, (depth + gain) - band.Exponent);
                foreach (JpxCodeBlock cb in band.CodeBlocks)
                {
                    if (cb.NumPasses <= 0)
                    {
                        continue;
                    }

                    JpxTier1.Result t1 = JpxTier1.Decode(cb, band, tileData);
                    int low = t1.LowestPlane;
                    int bw = cb.Width;
                    for (int sy = 0; sy < cb.Height; sy++)
                    {
                        for (int sx = 0; sx < bw; sx++)
                        {
                            int m = t1.Magnitude[(sy * bw) + sx];
                            if (m == 0)
                            {
                                continue;
                            }

                            double mag = m;
                            if (low > 0)
                            {
                                mag += 1 << (low - 1); // mid-point reconstruction for truncated planes
                            }

                            double val = (t1.Negative[(sy * bw) + sx] ? -mag : mag) * delta;
                            int lbx = (cb.X0 + sx) - band.X0;
                            int lby = (cb.Y0 + sy) - band.Y0;
                            int idx = PlaceIndex(res, band.Orientation, lbx, lby, px, py);
                            if ((uint)idx < (uint)data[r].Length)
                            {
                                data[r][idx] = val;
                            }
                        }
                    }
                }
            }
        }

        return JpxWavelet.SynthesizeIrreversible(tc.Resolutions, data);
    }

    private static int PlaceIndex(JpxResolution res, int orientation, int lbx, int lby, int px, int py)
    {
        if (orientation == 0)
        {
            return (lby * res.Width) + lbx;
        }

        int col;
        int row;
        switch (orientation)
        {
            case 1: // HL
                col = (2 * lbx) + (1 - px);
                row = (2 * lby) + py;
                break;
            case 2: // LH
                col = (2 * lbx) + px;
                row = (2 * lby) + (1 - py);
                break;
            default: // HH
                col = (2 * lbx) + (1 - px);
                row = (2 * lby) + (1 - py);
                break;
        }

        return (row * res.Width) + col;
    }

    private static void InverseRct(int[][] planes)
    {
        int n = Math.Min(planes[0].Length, Math.Min(planes[1].Length, planes[2].Length));
        for (int i = 0; i < n; i++)
        {
            int y = planes[0][i];
            int u = planes[1][i];
            int v = planes[2][i];
            int g = y - ((u + v) >> 2);
            planes[0][i] = v + g;
            planes[1][i] = g;
            planes[2][i] = u + g;
        }
    }

    private static void InverseIct(double[][] planes)
    {
        int n = Math.Min(planes[0].Length, Math.Min(planes[1].Length, planes[2].Length));
        for (int i = 0; i < n; i++)
        {
            double y = planes[0][i];
            double cb = planes[1][i];
            double cr = planes[2][i];
            planes[0][i] = y + (1.402 * cr);
            planes[1][i] = y - (0.344136 * cb) - (0.714136 * cr);
            planes[2][i] = y + (1.772 * cb);
        }
    }

    private static void WriteReversible(int[][] planes, JpxTileComponent[] tc, byte[][] channels, int imgW, int imgH)
    {
        for (int ch = 0; ch < channels.Length; ch++)
        {
            JpxTileComponent comp = tc[ch];
            int depth = comp.Siz.Depth;
            int shift = comp.Siz.Signed ? 0 : (1 << (depth - 1));
            int maxVal = (1 << depth) - 1;
            int cw = comp.Width;

            for (int y = comp.Y0 * comp.Siz.YRsiz, ey = comp.Y1 * comp.Siz.YRsiz; y < ey && y < imgH; y++)
            {
                int ly = (y / comp.Siz.YRsiz) - comp.Y0;
                if (ly < 0 || ly >= comp.Height)
                {
                    continue;
                }

                for (int x = comp.X0 * comp.Siz.XRsiz, ex = comp.X1 * comp.Siz.XRsiz; x < ex && x < imgW; x++)
                {
                    int lx = (x / comp.Siz.XRsiz) - comp.X0;
                    if (lx < 0 || lx >= cw)
                    {
                        continue;
                    }

                    int v = planes[ch][(ly * cw) + lx] + shift;
                    channels[ch][(y * imgW) + x] = To8Bit(v, depth, maxVal);
                }
            }
        }
    }

    private static void WriteIrreversible(double[][] planes, JpxTileComponent[] tc, byte[][] channels, int imgW, int imgH)
    {
        for (int ch = 0; ch < channels.Length; ch++)
        {
            JpxTileComponent comp = tc[ch];
            int depth = comp.Siz.Depth;
            double shift = comp.Siz.Signed ? 0 : (1 << (depth - 1));
            int maxVal = (1 << depth) - 1;
            int cw = comp.Width;

            for (int y = comp.Y0 * comp.Siz.YRsiz, ey = comp.Y1 * comp.Siz.YRsiz; y < ey && y < imgH; y++)
            {
                int ly = (y / comp.Siz.YRsiz) - comp.Y0;
                if (ly < 0 || ly >= comp.Height)
                {
                    continue;
                }

                for (int x = comp.X0 * comp.Siz.XRsiz, ex = comp.X1 * comp.Siz.XRsiz; x < ex && x < imgW; x++)
                {
                    int lx = (x / comp.Siz.XRsiz) - comp.X0;
                    if (lx < 0 || lx >= cw)
                    {
                        continue;
                    }

                    int v = (int)Math.Round(planes[ch][(ly * cw) + lx] + shift);
                    channels[ch][(y * imgW) + x] = To8Bit(v, depth, maxVal);
                }
            }
        }
    }

    private static byte To8Bit(int v, int depth, int maxVal)
    {
        if (v < 0)
        {
            v = 0;
        }
        else if (v > maxVal)
        {
            v = maxVal;
        }

        if (depth == 8)
        {
            return (byte)v;
        }

        return depth < 8 ? (byte)(v << (8 - depth)) : (byte)(v >> (depth - 8));
    }

    private static RasterImage Interleave(byte[][] channels, int w, int h, int outComps)
    {
        if (outComps == 1)
        {
            return new RasterImage { Width = w, Height = h, Components = 1, Samples = channels[0] };
        }

        var samples = new byte[w * h * 3];
        for (int i = 0; i < w * h; i++)
        {
            samples[(i * 3) + 0] = channels[0][i];
            samples[(i * 3) + 1] = channels[1][i];
            samples[(i * 3) + 2] = channels[2][i];
        }

        return new RasterImage { Width = w, Height = h, Components = 3, Samples = samples };
    }

    private static RasterImage Blank(int width, int height)
    {
        int w = Math.Max(1, width);
        int h = Math.Max(1, height);
        var samples = new byte[w * h];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = 255;
        }

        return new RasterImage { Width = w, Height = h, Components = 1, Samples = samples };
    }

    private static int GainLog2(int orientation)
        => orientation switch
        {
            0 => 0, // LL
            3 => 2, // HH
            _ => 1, // HL, LH
        };
}
