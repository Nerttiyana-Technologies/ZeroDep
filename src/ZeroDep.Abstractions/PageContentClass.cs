namespace ZeroDep.Abstractions;

/// <summary>
/// The structural class of a single page (ADR-0003) — a content-free routing signal derived only from how
/// the page is built (positioned text, AcroForm widgets, images/DPI, vector ruling lines), never from what
/// it says. <see cref="TableOrComplexLayout"/> is a hint only; ZeroDep emits positioned runs, not cells.
/// </summary>
public enum PageContentClass
{
    /// <summary>Positively blank — a content stream paints no marks, no widgets, no images.</summary>
    Empty = 0,

    /// <summary>Extractable digital text in a simple single-flow layout.</summary>
    DigitalText = 1,

    /// <summary>AcroForm widgets dominate the page (negligible non-widget table/prose).</summary>
    FormPage = 2,

    /// <summary>Text-bearing page with table/complex-layout structure (a routing hint, not cell extraction).</summary>
    TableOrComplexLayout = 3,

    /// <summary>A page-dominant image with no OCR text layer.</summary>
    ScannedImageOnly = 4,

    /// <summary>A page-dominant image with an invisible OCR text layer present.</summary>
    ScannedWithOcr = 5,

    /// <summary>Two or more independent content modes on one page (e.g. widgets plus a non-widget table).</summary>
    Mixed = 6,
}
