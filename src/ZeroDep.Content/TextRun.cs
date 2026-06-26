namespace ZeroDep.Content;

/// <summary>A run of shown text with its device-space origin, advance width, and text state.</summary>
internal sealed class TextRun
{
    public TextRun(string text, double x, double y, double width, double fontSize, int renderMode,
        int authoritativeChars, int fallbackChars, int unmappedChars)
    {
        Text = text;
        X = x;
        Y = y;
        Width = width;
        FontSize = fontSize;
        RenderMode = renderMode;
        AuthoritativeChars = authoritativeChars;
        FallbackChars = fallbackChars;
        UnmappedChars = unmappedChars;
    }

    /// <summary>The decoded text.</summary>
    public string Text { get; }

    /// <summary>Device-space X of the run origin.</summary>
    public double X { get; }

    /// <summary>Device-space Y of the run origin.</summary>
    public double Y { get; }

    /// <summary>Device-space advance width of the run (its right edge is X + Width when unrotated).</summary>
    public double Width { get; }

    /// <summary>Approximate device-space font size (run height).</summary>
    public double FontSize { get; }

    /// <summary>The text rendering mode (Tr).</summary>
    public int RenderMode { get; }

    /// <summary>True when drawn invisibly (Tr = 3) — the OCR/searchable layer.</summary>
    public bool IsOcrLayer => RenderMode == 3;

    /// <summary>Glyphs decoded via an authoritative map (ToUnicode / named encoding / Differences).</summary>
    public int AuthoritativeChars { get; }

    /// <summary>Glyphs decoded by a blind standard-encoding guess.</summary>
    public int FallbackChars { get; }

    /// <summary>Glyphs with no usable mapping (emitted empty / non-printable).</summary>
    public int UnmappedChars { get; }
}
