using System;
using System.Collections.Generic;

namespace ZeroDep.Filters.Jbig2;

/// <summary>
/// Decodes a JBIG2 generic region (ITU-T T.88 §6.2) with the MQ arithmetic coder. Supports all four
/// GBTEMPLATEs, adaptive (AT) pixels, and typical-prediction (TPGDON). The context is formed from the
/// combined coding+AT template in (y, x) order — matching the reference decoders.
/// </summary>
internal static class Jbig2GenericRegion
{
    // Base coding-template pixels per GBTEMPLATE (the AT pixels are appended from the segment).
    private static readonly (int X, int Y)[][] Templates =
    {
        new[] { (-1, -2), (0, -2), (1, -2), (-2, -1), (-1, -1), (0, -1), (1, -1), (2, -1), (-4, 0), (-3, 0), (-2, 0), (-1, 0) },
        new[] { (-1, -2), (0, -2), (1, -2), (2, -2), (-2, -1), (-1, -1), (0, -1), (1, -1), (2, -1), (-3, 0), (-2, 0), (-1, 0) },
        new[] { (-1, -2), (0, -2), (1, -2), (-2, -1), (-1, -1), (0, -1), (1, -1), (-2, 0), (-1, 0) },
        new[] { (-3, -1), (-2, -1), (-1, -1), (0, -1), (1, -1), (-4, 0), (-3, 0), (-2, 0), (-1, 0) },
    };

    // SLTP pseudo-pixel context per template (for TPGDON typical prediction).
    private static readonly int[] Sltp = { 0x9B25, 0x0795, 0x00E5, 0x0195 };

    public static Jbig2Bitmap Decode(
        MqDecoder mq, ArithContext cx, int width, int height, int template, (int X, int Y)[] at, bool tpgdon)
    {
        var combined = new List<(int X, int Y)>(Templates[template]);
        combined.AddRange(at);
        combined.Sort((a, b) => a.Y != b.Y ? a.Y - b.Y : a.X - b.X);
        (int X, int Y)[] tpl = combined.ToArray();
        int n = tpl.Length;

        var bmp = new Jbig2Bitmap(width, height);
        byte[] data = bmp.Data;

        // Precompute, in the template's bit order, the flat-array deltas and the bounding offsets that
        // define where every template pixel is guaranteed in-bounds (the fast interior path).
        var delta = new int[n];
        int up = 0, left = 0, rightExt = 0;
        for (int t = 0; t < n; t++)
        {
            delta[t] = (tpl[t].Y * width) + tpl[t].X;
            if (-tpl[t].Y > up)
            {
                up = -tpl[t].Y;
            }

            if (-tpl[t].X > left)
            {
                left = -tpl[t].X;
            }

            if (tpl[t].X > rightExt)
            {
                rightExt = tpl[t].X;
            }
        }

        int interiorXEnd = width - rightExt;
        int ltp = 0;

        for (int y = 0; y < height; y++)
        {
            if (tpgdon)
            {
                ltp ^= mq.Decode(cx, Sltp[template]);
                if (ltp == 1)
                {
                    if (y > 0)
                    {
                        Array.Copy(data, (y - 1) * width, data, y * width, width);
                    }

                    continue;
                }
            }

            int rowBase = y * width;
            bool interiorRow = y >= up;
            for (int x = 0; x < width; x++)
            {
                int context;
                if (interiorRow && x >= left && x < interiorXEnd)
                {
                    // Fast path: every template pixel is in-bounds — read the array directly.
                    int baseIdx = rowBase + x;
                    context = 0;
                    for (int t = 0; t < n; t++)
                    {
                        context = (context << 1) | data[baseIdx + delta[t]];
                    }
                }
                else
                {
                    context = 0;
                    for (int t = 0; t < n; t++)
                    {
                        context = (context << 1) | bmp.Get(x + tpl[t].X, y + tpl[t].Y);
                    }
                }

                if (mq.Decode(cx, context) != 0)
                {
                    data[rowBase + x] = 1;
                }
            }
        }

        return bmp;
    }
}
