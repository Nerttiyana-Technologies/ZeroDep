using System;

namespace ZeroDep.Abstractions;

/// <summary>
/// An embedded image XObject extracted from a PDF: its page, declared dimensions, filter, and the
/// raw (still-encoded) image bytes. For a single <c>/DCTDecode</c> filter, <see cref="EncodedData"/>
/// is the raw JPEG and can be passed straight to the JPEG decoder.
/// </summary>
public sealed class PdfImageInfo
{
    /// <summary>The zero-based index of the page the image appears on.</summary>
    public int PageIndex { get; init; }

    /// <summary>The image's declared pixel width (<c>/Width</c>).</summary>
    public int DeclaredWidth { get; init; }

    /// <summary>The image's declared pixel height (<c>/Height</c>).</summary>
    public int DeclaredHeight { get; init; }

    /// <summary>The image's <c>/Filter</c> (filters joined with <c>+</c>), or null if none.</summary>
    public string? Filter { get; init; }

    /// <summary>
    /// The CCITT decode parameters (from <c>/DecodeParms</c>) when this is a single-filter
    /// <c>/CCITTFaxDecode</c> image; otherwise null.
    /// </summary>
    public CcittParameters? Ccitt { get; init; }

    /// <summary>
    /// The decoded <c>JBIG2Globals</c> stream (shared symbol dictionaries) for a <c>/JBIG2Decode</c>
    /// image that references one; otherwise null.
    /// </summary>
    public byte[]? Jbig2Globals { get; init; }

    /// <summary>The raw, still-encoded image bytes (e.g. the JPEG stream for a <c>/DCTDecode</c> image).</summary>
    public byte[] EncodedData { get; init; } = Array.Empty<byte>();

    /// <summary>The image's <c>/BitsPerComponent</c>, or 0 if absent.</summary>
    public int BitsPerComponent { get; init; }

    /// <summary>
    /// The resolved colour-space family (e.g. <c>DeviceRGB</c>, <c>DeviceCMYK</c>, <c>Indexed</c>,
    /// <c>ICCBased</c>, <c>Separation</c>, <c>Lab</c>), or null if absent/unresolved.
    /// </summary>
    public string? ColorSpaceFamily { get; init; }

    /// <summary>The number of colour components the colour space carries, or 0 if unknown.</summary>
    public int ColorComponents { get; init; }

    /// <summary>The image's <c>/Decode</c> array, or null if absent.</summary>
    public double[]? Decode { get; init; }

    /// <summary>Whether the image carries a soft mask (<c>/SMask</c>) — captured for later alpha compositing.</summary>
    public bool HasSoftMask { get; init; }
}

/// <summary>
/// The <c>/CCITTFaxDecode</c> parameters captured from a PDF image's <c>/DecodeParms</c>, with PDF
/// defaults applied (and <c>Columns</c>/<c>Rows</c> falling back to the image's declared size).
/// </summary>
public sealed class CcittParameters
{
    /// <summary>Coding scheme: <c>K &lt; 0</c> Group 4; <c>K = 0</c> Group 3 1D; <c>K &gt; 0</c> Group 3 2D.</summary>
    public int K { get; init; }

    /// <summary>Pixels per row.</summary>
    public int Columns { get; init; }

    /// <summary>Number of rows; 0 means "until end of data".</summary>
    public int Rows { get; init; }

    /// <summary>When true, 1 bits are black (reverse of the normal convention).</summary>
    public bool BlackIs1 { get; init; }

    /// <summary>When true, each encoded row is padded to a byte boundary.</summary>
    public bool EncodedByteAlign { get; init; }
}
