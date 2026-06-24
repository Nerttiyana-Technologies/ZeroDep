using System;

namespace ZeroDep.Ocr;

/// <summary>
/// A decoded raster image produced by ZeroDep's pure-BCL image decoders and handed to an
/// <see cref="IOcrEngine"/>. Pixels are row-major, top-to-bottom, in the declared <see cref="Format"/>.
/// A plain <see cref="byte"/> array is used (rather than <c>Memory&lt;byte&gt;</c>) so the type stays
/// dependency-free on <c>netstandard2.0</c>.
/// </summary>
public sealed class DecodedImage
{
    /// <summary>Image width in pixels.</summary>
    public int Width { get; init; }

    /// <summary>Image height in pixels.</summary>
    public int Height { get; init; }

    /// <summary>The effective resolution (dots per inch) the image is placed at, where known; 0 if unknown.</summary>
    public int Dpi { get; init; }

    /// <summary>The sample layout of <see cref="Pixels"/>.</summary>
    public PixelFormat Format { get; init; }

    /// <summary>The raw pixel bytes, row-major and top-to-bottom in <see cref="Format"/> order.</summary>
    public byte[] Pixels { get; init; } = Array.Empty<byte>();
}
