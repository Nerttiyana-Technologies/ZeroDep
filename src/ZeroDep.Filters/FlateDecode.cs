using System;
using System.IO;
using System.IO.Compression;

namespace ZeroDep.Filters;

/// <summary>
/// Decodes PDF <c>/FlateDecode</c> data (ISO 32000-2 §7.4.4) using only the BCL.
/// PDF Flate data is zlib-wrapped (RFC 1950); the 2-byte zlib header is stripped
/// and the underlying DEFLATE payload (RFC 1951) is inflated via <see cref="DeflateStream"/>.
/// </summary>
public static class FlateDecode
{
    /// <summary>
    /// Inflates a FlateDecode buffer and applies an optional PNG/TIFF predictor.
    /// </summary>
    /// <param name="input">The raw (compressed) stream bytes.</param>
    /// <param name="predictor">The <c>/Predictor</c> value (1 = none).</param>
    /// <param name="colors">The <c>/Colors</c> value (samples per pixel).</param>
    /// <param name="bitsPerComponent">The <c>/BitsPerComponent</c> value.</param>
    /// <param name="columns">The <c>/Columns</c> value (samples per row).</param>
    /// <returns>The decoded bytes.</returns>
    public static byte[] Decode(byte[] input, int predictor = 1, int colors = 1, int bitsPerComponent = 8, int columns = 1)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));

        byte[] inflated = Inflate(input);
        return predictor <= 1
            ? inflated
            : Predictor.Apply(inflated, predictor, colors, bitsPerComponent, columns);
    }

    private static byte[] Inflate(byte[] input)
    {
        if (input.Length == 0) return Array.Empty<byte>();

        int offset = HasZlibHeader(input) ? 2 : 0;
        using var source = new MemoryStream(input, offset, input.Length - offset, writable: false);
        using var deflate = new DeflateStream(source, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>
    /// Detects a 2-byte zlib (RFC 1950) header (e.g. <c>0x78 0x9C</c>): the low nibble of the
    /// first byte must indicate DEFLATE (8) and the two bytes together must be a multiple of 31.
    /// </summary>
    internal static bool HasZlibHeader(byte[] data)
    {
        if (data.Length < 2) return false;
        int cmf = data[0];
        int flg = data[1];
        if ((cmf & 0x0F) != 8) return false;
        return ((cmf << 8) | flg) % 31 == 0;
    }
}
