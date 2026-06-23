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

    /// <summary>True when drawn invisibly (Tr = 3) — the OCR/searchable layer.</summary>
    public bool IsOcrLayer { get; init; }
}
