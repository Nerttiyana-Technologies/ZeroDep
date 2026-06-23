using System;

namespace ZeroDep.Filters;

/// <summary>
/// Reverses PNG and TIFF predictors applied before Flate/LZW compression
/// (ISO 32000-2 §7.4.4.4, "Predictor functions").
/// </summary>
internal static class Predictor
{
    /// <summary>Reverses the predictor on <paramref name="data"/>; predictor &lt; 2 returns the data unchanged.</summary>
    public static byte[] Apply(byte[] data, int predictor, int colors, int bitsPerComponent, int columns)
    {
        if (predictor < 2 || data.Length == 0) return data;

        int bytesPerPixel = Math.Max(1, (colors * bitsPerComponent + 7) / 8);
        int rowLength = (colors * bitsPerComponent * columns + 7) / 8;
        if (rowLength <= 0) return data;

        return predictor == 2
            ? ApplyTiff(data, colors, bitsPerComponent, rowLength)
            : ApplyPng(data, bytesPerPixel, rowLength);
    }

    private static byte[] ApplyPng(byte[] data, int bytesPerPixel, int stride)
    {
        // Each PNG-predicted row is prefixed with a one-byte filter type.
        int rows = data.Length / (stride + 1);
        var output = new byte[rows * stride];
        var previous = new byte[stride];

        int inPos = 0;
        int outPos = 0;
        for (int r = 0; r < rows; r++)
        {
            int filter = data[inPos++];
            var current = new byte[stride];
            Array.Copy(data, inPos, current, 0, stride);
            inPos += stride;

            for (int i = 0; i < stride; i++)
            {
                int a = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                int b = previous[i];
                int c = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
                int x = current[i];
                int value;
                switch (filter)
                {
                    case 0: value = x; break;                       // None
                    case 1: value = x + a; break;                   // Sub
                    case 2: value = x + b; break;                   // Up
                    case 3: value = x + ((a + b) >> 1); break;      // Average
                    case 4: value = x + Paeth(a, b, c); break;      // Paeth
                    default: value = x; break;
                }
                current[i] = (byte)(value & 0xFF);
            }

            Array.Copy(current, 0, output, outPos, stride);
            outPos += stride;
            previous = current;
        }
        return output;
    }

    private static byte[] ApplyTiff(byte[] data, int colors, int bitsPerComponent, int stride)
    {
        // TIFF predictor 2 is defined per-component; this implementation covers the common 8-bit case.
        if (bitsPerComponent != 8) return data;

        var output = (byte[])data.Clone();
        int rows = stride == 0 ? 0 : data.Length / stride;
        for (int r = 0; r < rows; r++)
        {
            int rowStart = r * stride;
            for (int i = colors; i < stride; i++)
            {
                output[rowStart + i] = (byte)((output[rowStart + i] + output[rowStart + i - colors]) & 0xFF);
            }
        }
        return output;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }
}
