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

    /// <summary>The raw, still-encoded image bytes (e.g. the JPEG stream for a <c>/DCTDecode</c> image).</summary>
    public byte[] EncodedData { get; init; } = Array.Empty<byte>();
}
