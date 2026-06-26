using System;
using ZeroDep.Filters;

namespace ZeroDep.Color;

/// <summary>
/// Normalizes decoded image samples to RGB using a resolved <see cref="PdfColorSpace"/> (ADR-0004 §2.2).
/// Honors the image's bits-per-component (1/2/4/8/16, row-byte-aligned) and the optional <c>/Decode</c>
/// array. Output is a 3-component <see cref="RasterImage"/> (RGB), or a 1-component grayscale raster for
/// DeviceGray/CalGray. Pure-BCL and deterministic.
/// </summary>
public static class ColorConverter
{
    /// <summary>
    /// Normalizes packed sample data (native bit depth, rows byte-aligned) to an RGB/Gray raster.
    /// </summary>
    /// <param name="data">The decoded, still-packed image samples.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="components">Components per pixel (must match the colour space, except 16-bit reductions).</param>
    /// <param name="bitsPerComponent">Bits per component: 1, 2, 4, 8, or 16.</param>
    /// <param name="colorSpace">The resolved colour space.</param>
    /// <param name="decode">The image's <c>/Decode</c> array, or null for the colour space default.</param>
    public static RasterImage ToRgb(
        byte[] data, int width, int height, int components, int bitsPerComponent, PdfColorSpace colorSpace, double[]? decode = null)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (colorSpace is null)
        {
            throw new ArgumentNullException(nameof(colorSpace));
        }

        if (width <= 0 || height <= 0 || components <= 0 || bitsPerComponent <= 0)
        {
            return new RasterImage { Width = Math.Max(1, width), Height = Math.Max(1, height), Components = 1, Samples = Array.Empty<byte>() };
        }

        double[] dec = decode is { Length: > 0 } ? decode : colorSpace.DefaultDecode(bitsPerComponent);
        double maxVal = bitsPerComponent >= 31 ? int.MaxValue : (1 << bitsPerComponent) - 1.0;
        bool gray = colorSpace.IsGrayscale && colorSpace.ComponentCount == 1 && components == 1;

        int compBufLen = Math.Max(components, colorSpace.ComponentCount);
        var comp = new double[compBufLen];
        long rowStrideBits = (((long)width * components * bitsPerComponent + 7) / 8) * 8;

        if (gray)
        {
            var outGray = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    long bitPos = (y * rowStrideBits) + ((long)x * bitsPerComponent);
                    int raw = ReadBits(data, bitPos, bitsPerComponent);
                    double v = dec[0] + ((raw / maxVal) * (dec[1] - dec[0]));
                    outGray[(y * width) + x] = ToByte(v);
                }
            }

            return new RasterImage { Width = width, Height = height, Components = 1, Samples = outGray };
        }

        var outRgb = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                long baseBit = (y * rowStrideBits) + ((long)x * components * bitsPerComponent);
                for (int c = 0; c < components; c++)
                {
                    int raw = ReadBits(data, baseBit + ((long)c * bitsPerComponent), bitsPerComponent);
                    double dlo = (2 * c) < dec.Length ? dec[2 * c] : 0.0;
                    double dhi = ((2 * c) + 1) < dec.Length ? dec[(2 * c) + 1] : 1.0;
                    comp[c] = dlo + ((raw / maxVal) * (dhi - dlo));
                }

                colorSpace.ToRgb(comp, 0, out byte r, out byte g, out byte b);
                int o = ((y * width) + x) * 3;
                outRgb[o] = r;
                outRgb[o + 1] = g;
                outRgb[o + 2] = b;
            }
        }

        return new RasterImage { Width = width, Height = height, Components = 3, Samples = outRgb };
    }

    /// <summary>
    /// Normalizes an already-decoded 8-bit raster (e.g. a codec's output) through a colour space. The
    /// raster's <see cref="RasterImage.Components"/> are treated as raw colour components.
    /// </summary>
    /// <param name="decoded">The decoded 8-bit raster.</param>
    /// <param name="colorSpace">The resolved colour space.</param>
    /// <param name="decode">The image's <c>/Decode</c> array, or null for the colour space default.</param>
    public static RasterImage ToRgb(RasterImage decoded, PdfColorSpace colorSpace, double[]? decode = null)
    {
        if (decoded is null)
        {
            throw new ArgumentNullException(nameof(decoded));
        }

        return ToRgb(decoded.Samples, decoded.Width, decoded.Height, decoded.Components, 8, colorSpace, decode);
    }

    private static int ReadBits(byte[] data, long bitPos, int n)
    {
        if (n == 8)
        {
            int bp = (int)(bitPos >> 3);
            return bp >= 0 && bp < data.Length ? data[bp] : 0;
        }

        int v = 0;
        for (int i = 0; i < n; i++)
        {
            long bp = bitPos + i;
            int bytePos = (int)(bp >> 3);
            int bit = 7 - (int)(bp & 7);
            int b = bytePos >= 0 && bytePos < data.Length ? (data[bytePos] >> bit) & 1 : 0;
            v = (v << 1) | b;
        }

        return v;
    }

    private static byte ToByte(double v01)
    {
        int x = (int)Math.Round(v01 * 255.0, MidpointRounding.AwayFromZero);
        return (byte)(x < 0 ? 0 : (x > 255 ? 255 : x));
    }
}
