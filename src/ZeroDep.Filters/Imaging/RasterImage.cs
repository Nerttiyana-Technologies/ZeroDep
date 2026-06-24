using System;

namespace ZeroDep.Filters;

/// <summary>
/// A decoded raster image: interleaved 8-bit samples, row-major and top-to-bottom. Produced by the
/// pure-BCL <see cref="JpegDecoder"/> and (later) handed to the OCR layer.
/// </summary>
public sealed class RasterImage
{
    /// <summary>Image width in pixels.</summary>
    public int Width { get; init; }

    /// <summary>Image height in pixels.</summary>
    public int Height { get; init; }

    /// <summary>Samples per pixel: 1 (grayscale) or 3 (RGB).</summary>
    public int Components { get; init; }

    /// <summary>Interleaved 8-bit samples; length is <c>Width * Height * Components</c>.</summary>
    public byte[] Samples { get; init; } = Array.Empty<byte>();
}
