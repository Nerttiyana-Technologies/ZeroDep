using System;
using ZeroDep.Filters.Jbig2;

namespace ZeroDep.Filters.Jpx;

/// <summary>
/// Tier-1 of the JPEG 2000 decoder (ITU-T T.800 Annex D): the EBCOT bit-plane coder. For one code-block
/// it runs the significance-propagation, magnitude-refinement and cleanup passes over the gathered MQ
/// data and returns the signed quantizer indices. The MQ arithmetic engine is reused from the JBIG2
/// decoder (<see cref="MqDecoder"/>), the same coder ITU-T T.88 and T.800 share.
/// </summary>
internal static class JpxTier1
{
    // Context labels (matching the OpenJPEG/T.800 grouping): ZC 0-8, SC 9-13, MR 14-16, RUN 17, UNI 18.
    private const int CtxRun = 17;
    private const int CtxUni = 18;
    private const int NumContexts = 19;

    /// <summary>The decoded magnitudes (quantizer indices) and signs for a code-block.</summary>
    internal struct Result
    {
        public int[] Magnitude;  // unsigned quantizer index per coefficient
        public bool[] Negative;  // sign per coefficient
        public int LowestPlane;  // lowest bit-plane index reached (for reconstruction)
    }

    public static Result Decode(JpxCodeBlock cb, JpxSubband band, byte[] data)
    {
        int w = cb.Width;
        int h = cb.Height;
        var mag = new int[w * h];
        var negative = new bool[w * h];
        var sig = new bool[w * h];
        var visited = new bool[w * h];
        var refined = new bool[w * h];

        if (cb.NumPasses <= 0 || w <= 0 || h <= 0)
        {
            return new Result { Magnitude = mag, Negative = negative, LowestPlane = 0 };
        }

        // Concatenate the block's segments into one MQ stream (valid for the default code-block style).
        byte[] buffer = Concatenate(cb, data);
        var mq = new MqDecoder(buffer, 0, buffer.Length);
        var cx = NewContexts();

        int group = OrientationGroup(band.Orientation);
        int numbps = band.NumBps - cb.ZeroBitPlanes;
        if (numbps <= 0)
        {
            return new Result { Magnitude = mag, Negative = negative, LowestPlane = 0 };
        }

        int bp = numbps - 1;
        for (int pass = 0; pass < cb.NumPasses && bp >= 0; pass++)
        {
            int kind = pass == 0 ? 2 : ((pass - 1) % 3); // 2=cleanup for the first pass
            if (pass != 0 && kind == 0)
            {
                bp--;
                if (bp < 0)
                {
                    break;
                }
            }

            switch (kind)
            {
                case 0:
                    Significance(mq, cx, w, h, bp, group, sig, negative, mag, visited);
                    break;
                case 1:
                    Refinement(mq, cx, w, h, bp, sig, mag, visited, refined);
                    break;
                default:
                    Cleanup(mq, cx, w, h, bp, group, sig, negative, mag, visited);
                    break;
            }
        }

        return new Result { Magnitude = mag, Negative = negative, LowestPlane = Math.Max(0, bp) };
    }

    private static void Significance(
        MqDecoder mq, ArithContext cx, int w, int h, int bp, int group,
        bool[] sig, bool[] negative, int[] mag, bool[] visited)
    {
        for (int y0 = 0; y0 < h; y0 += 4)
        {
            int yEnd = Math.Min(y0 + 4, h);
            for (int x = 0; x < w; x++)
            {
                for (int y = y0; y < yEnd; y++)
                {
                    int i = (y * w) + x;
                    visited[i] = false;
                    if (sig[i] || !HasSignificantNeighbor(sig, w, h, x, y))
                    {
                        continue;
                    }

                    int ctx = ZeroContext(sig, w, h, x, y, group);
                    if (mq.Decode(cx, ctx) == 1)
                    {
                        negative[i] = DecodeSign(mq, cx, sig, negative, w, h, x, y) == 1;
                        sig[i] = true;
                        mag[i] |= 1 << bp;
                    }

                    visited[i] = true;
                }
            }
        }
    }

    private static void Refinement(
        MqDecoder mq, ArithContext cx, int w, int h, int bp,
        bool[] sig, int[] mag, bool[] visited, bool[] refined)
    {
        for (int y0 = 0; y0 < h; y0 += 4)
        {
            int yEnd = Math.Min(y0 + 4, h);
            for (int x = 0; x < w; x++)
            {
                for (int y = y0; y < yEnd; y++)
                {
                    int i = (y * w) + x;
                    if (!sig[i] || visited[i])
                    {
                        continue;
                    }

                    int ctx;
                    if (!refined[i])
                    {
                        ctx = NeighborSum(sig, w, h, x, y) > 0 ? 15 : 14;
                    }
                    else
                    {
                        ctx = 16;
                    }

                    int bit = mq.Decode(cx, ctx);
                    mag[i] |= bit << bp;
                    refined[i] = true;
                    visited[i] = true;
                }
            }
        }
    }

    private static void Cleanup(
        MqDecoder mq, ArithContext cx, int w, int h, int bp, int group,
        bool[] sig, bool[] negative, int[] mag, bool[] visited)
    {
        for (int y0 = 0; y0 < h; y0 += 4)
        {
            int yEnd = Math.Min(y0 + 4, h);
            for (int x = 0; x < w; x++)
            {
                int y = y0;

                // Run mode: only for a full stripe of four coefficients, all insignificant, unvisited,
                // and with no significant neighbour.
                if (yEnd - y0 == 4 && RunEligible(sig, visited, w, h, x, y0))
                {
                    if (mq.Decode(cx, CtxRun) == 0)
                    {
                        continue; // all four stay insignificant
                    }

                    int pos = (mq.Decode(cx, CtxUni) << 1) | mq.Decode(cx, CtxUni);
                    y = y0 + pos;
                    int i = (y * w) + x;
                    negative[i] = DecodeSign(mq, cx, sig, negative, w, h, x, y) == 1;
                    sig[i] = true;
                    mag[i] |= 1 << bp;
                    y++;
                }

                for (; y < yEnd; y++)
                {
                    int i = (y * w) + x;
                    if (sig[i] || visited[i])
                    {
                        continue;
                    }

                    int ctx = ZeroContext(sig, w, h, x, y, group);
                    if (mq.Decode(cx, ctx) == 1)
                    {
                        negative[i] = DecodeSign(mq, cx, sig, negative, w, h, x, y) == 1;
                        sig[i] = true;
                        mag[i] |= 1 << bp;
                    }
                }
            }
        }

        // Visited flags are scoped to a bit-plane; clear them for the next significance pass.
        Array.Clear(visited, 0, visited.Length);
    }

    private static bool RunEligible(bool[] sig, bool[] visited, int w, int h, int x, int y0)
    {
        for (int y = y0; y < y0 + 4; y++)
        {
            int i = (y * w) + x;
            if (sig[i] || visited[i] || HasSignificantNeighbor(sig, w, h, x, y))
            {
                return false;
            }
        }

        return true;
    }

    // Sign coding (T.800 Table D.5/D.6): horizontal and vertical sign contributions select context+XOR.
    private static int DecodeSign(MqDecoder mq, ArithContext cx, bool[] sig, bool[] negative, int w, int h, int x, int y)
    {
        int hc = SignContribution(sig, negative, w, h, x - 1, y) + SignContribution(sig, negative, w, h, x + 1, y);
        int vc = SignContribution(sig, negative, w, h, x, y - 1) + SignContribution(sig, negative, w, h, x, y + 1);
        hc = Clamp1(hc);
        vc = Clamp1(vc);

        int ctx = SignContext[hc + 1, vc + 1];
        int xorBit = SignXor[hc + 1, vc + 1];
        return mq.Decode(cx, ctx) ^ xorBit;
    }

    private static int SignContribution(bool[] sig, bool[] negative, int w, int h, int x, int y)
    {
        if (x < 0 || y < 0 || x >= w || y >= h)
        {
            return 0;
        }

        int i = (y * w) + x;
        if (!sig[i])
        {
            return 0;
        }

        return negative[i] ? -1 : 1;
    }

    private static bool HasSignificantNeighbor(bool[] sig, int w, int h, int x, int y)
        => NeighborSum(sig, w, h, x, y) > 0;

    private static int NeighborSum(bool[] sig, int w, int h, int x, int y)
    {
        int s = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            int ny = y + dy;
            if (ny < 0 || ny >= h)
            {
                continue;
            }

            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                int nx = x + dx;
                if (nx < 0 || nx >= w)
                {
                    continue;
                }

                if (sig[(ny * w) + nx])
                {
                    s++;
                }
            }
        }

        return s;
    }

    // Zero coding (T.800 Tables D.1-D.3). group: 0=LL/LH, 1=HL, 2=HH.
    private static int ZeroContext(bool[] sig, int w, int h, int x, int y, int group)
    {
        int hCount = Count(sig, w, h, x - 1, y) + Count(sig, w, h, x + 1, y);
        int vCount = Count(sig, w, h, x, y - 1) + Count(sig, w, h, x, y + 1);
        int dCount = Count(sig, w, h, x - 1, y - 1) + Count(sig, w, h, x + 1, y - 1)
                     + Count(sig, w, h, x - 1, y + 1) + Count(sig, w, h, x + 1, y + 1);

        if (group == 2)
        {
            int hv = hCount + vCount;
            if (dCount >= 3)
            {
                return 8;
            }

            if (dCount == 2)
            {
                return hv >= 1 ? 7 : 6;
            }

            if (dCount == 1)
            {
                return hv >= 2 ? 5 : (hv == 1 ? 4 : 3);
            }

            return hv >= 2 ? 2 : (hv == 1 ? 1 : 0);
        }

        int a = group == 1 ? vCount : hCount;
        int b = group == 1 ? hCount : vCount;
        if (a == 2)
        {
            return 8;
        }

        if (a == 1)
        {
            return b >= 1 ? 7 : (dCount >= 1 ? 6 : 5);
        }

        if (b == 2)
        {
            return 4;
        }

        if (b == 1)
        {
            return 3;
        }

        if (dCount >= 2)
        {
            return 2;
        }

        return dCount == 1 ? 1 : 0;
    }

    private static int Count(bool[] sig, int w, int h, int x, int y)
        => x < 0 || y < 0 || x >= w || y >= h ? 0 : (sig[(y * w) + x] ? 1 : 0);

    private static int OrientationGroup(int orientation)
        => orientation switch
        {
            1 => 1, // HL
            3 => 2, // HH
            _ => 0, // LL, LH
        };

    private static ArithContext NewContexts()
    {
        var cx = new ArithContext(NumContexts);
        // T.800 initial states: ZC ctx0 -> 4, RUN -> 3, UNI -> 46; all others 0; all MPS 0.
        cx.Index[0] = 4;
        cx.Index[CtxRun] = 3;
        cx.Index[CtxUni] = 46;
        return cx;
    }

    private static byte[] Concatenate(JpxCodeBlock cb, byte[] data)
    {
        int total = 0;
        foreach ((int _, int len) in cb.Segments)
        {
            total += len;
        }

        var buffer = new byte[total];
        int p = 0;
        foreach ((int off, int len) in cb.Segments)
        {
            if (len > 0 && off >= 0 && off + len <= data.Length)
            {
                Array.Copy(data, off, buffer, p, len);
            }

            p += len;
        }

        return buffer;
    }

    private static int Clamp1(int v) => v < -1 ? -1 : (v > 1 ? 1 : v);

    // Indexed by [hc+1, vc+1].
    private static readonly int[,] SignContext =
    {
        { 13, 12, 11 }, // hc = -1
        { 10, 9, 10 },  // hc = 0
        { 11, 12, 13 }, // hc = +1
    };

    private static readonly int[,] SignXor =
    {
        { 1, 1, 1 }, // hc = -1
        { 1, 0, 0 }, // hc = 0
        { 0, 0, 0 }, // hc = +1
    };
}
