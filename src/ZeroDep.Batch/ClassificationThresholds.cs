namespace ZeroDep.Batch;

/// <summary>
/// Reproducible, content-free thresholds for structural document classification (ADR Feature H).
/// These are reported alongside published statistics so the classification is reproducible and
/// never derives from document content.
/// </summary>
public sealed class ClassificationThresholds
{
    /// <summary>
    /// A page is "image-dominated" when raster images cover at least this percentage of its area.
    /// </summary>
    public int ImageAreaPercent { get; init; } = 80;

    /// <summary>
    /// The minimum number of real (visible) text characters for a document to count as carrying
    /// extractable text. Invisible OCR-layer characters do not count toward this total.
    /// </summary>
    public int MinTextChars { get; init; } = 24;

    /// <summary>The default thresholds (80% image area, 24 minimum text characters).</summary>
    public static ClassificationThresholds Default { get; } = new ClassificationThresholds();
}
