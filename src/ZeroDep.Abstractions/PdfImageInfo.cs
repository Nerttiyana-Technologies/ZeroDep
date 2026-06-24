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

    /// <summary>The raw, still-encoded image bytes (e.g. the JPEG stream for a <c>/DCTDecode</c> image).</summary>
    public byte[] EncodedData { get; init; } = Array.Empty<byte>();
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
