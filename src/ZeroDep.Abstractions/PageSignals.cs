namespace ZeroDep.Abstractions;

/// <summary>
/// The deterministic structural evidence behind a page's <see cref="PageContentClass"/> (ADR-0003 §2.1),
/// so a consumer can apply its own routing policy. All signals are derived from the object graph and
/// content-stream operators in one pass; the ratio signals are computed deterministically (fixed rounding).
/// </summary>
public sealed class PageSignals
{
    /// <summary>Number of visible (non-OCR-layer) embedded text runs on the page.</summary>
    public int TextRunCount { get; init; }

    /// <summary>Fraction of the page area covered by visible text run boxes (0–1, rounded).</summary>
    public double TextCoverageFraction { get; init; }

    /// <summary>Whether the page carries any AcroForm widgets.</summary>
    public bool HasAcroFormWidgets { get; init; }

    /// <summary>Number of AcroForm widgets on the page.</summary>
    public int WidgetCount { get; init; }

    /// <summary>Whether a single image dominates the page area (a scanned-page signal).</summary>
    public bool IsImageOnly { get; init; }

    /// <summary>Whether an invisible OCR text layer (Tr 3) or OCR-generated text is present.</summary>
    public bool OcrLayerPresent { get; init; }

    /// <summary>The minimum effective DPI among images on the page, or 0 if none.</summary>
    public double MinImageDpi { get; init; }

    /// <summary>Count of axis-aligned vector ruling lines (from path operators) — a table/complex hint.</summary>
    public int RulingLineCount { get; init; }

    /// <summary>Aligned-run density (0–1, rounded) — the columnar/tabular text hint.</summary>
    public double ColumnAlignmentScore { get; init; }

    /// <summary>Number of distinct fonts selected on the page.</summary>
    public int FontDistinctCount { get; init; }
}
