namespace ZeroDep.Abstractions;

/// <summary>A run of extracted text with its position and state (Feature B).</summary>
public sealed class TextRunInfo
{
    /// <summary>Zero-based page index.</summary>
    public int PageIndex { get; init; }

    /// <summary>The decoded text.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Device-space X of the run origin.</summary>
    public double X { get; init; }

    /// <summary>Device-space Y of the run origin (PDF space: larger is higher on the page).</summary>
    public double Y { get; init; }

    /// <summary>Device-space advance width (right edge is X + Width when unrotated).</summary>
    public double Width { get; init; }

    /// <summary>Approximate device-space font size.</summary>
    public double FontSize { get; init; }

    /// <summary>The text rendering mode (Tr).</summary>
    public int RenderMode { get; init; }

    /// <summary>True when drawn invisibly (Tr = 3) — the embedded OCR/searchable layer.</summary>
    public bool IsOcrLayer { get; init; }

    /// <summary>The provenance of this run: embedded in the document, or recovered by OCR.</summary>
    public TextSource Source { get; init; } = TextSource.Embedded;

    /// <summary>
    /// Recognition confidence (0–1) for <see cref="TextSource.OcrGenerated"/> runs; 1.0 for embedded
    /// text. Lets consumers threshold OCR output rather than trusting it blindly.
    /// </summary>
    public double Confidence { get; init; } = 1.0;

    /// <summary>Glyphs in this run decoded via an authoritative map (ToUnicode / named encoding / Differences).</summary>
    public int AuthoritativeChars { get; init; }

    /// <summary>Glyphs decoded by a blind standard-encoding guess (no map) — the wrong-decode risk (ADR-0007).</summary>
    public int FallbackChars { get; init; }

    /// <summary>Glyphs with no usable mapping (emitted empty / non-printable).</summary>
    public int UnmappedChars { get; init; }

    /// <summary>
    /// The font's space advance in em (1.0 = full em). Lets a run-joiner detect inter-word gaps encoded
    /// positionally (no space glyph) by comparing the gap to the font's own space width (ADR-0008).
    /// Defaults to 0.25 when unknown.
    /// </summary>
    public double SpaceWidthEm { get; init; } = 0.25;
}
