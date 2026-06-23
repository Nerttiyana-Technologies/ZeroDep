using System;
using System.Collections.Generic;

namespace ZeroDep.Batch;

/// <summary>
/// The publishable aggregate statistics for a corpus run (ADR §8.2). This type is the privacy
/// boundary: by construction it has <b>no field</b> capable of carrying a file name, extracted text,
/// form field name, form field value, or document metadata string. Only aggregates and counts,
/// categorized by structural traits, can be represented here.
/// </summary>
public sealed class AggregateStatistics
{
    /// <summary>The aggregate-schema version.</summary>
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>The UTC timestamp the statistics were generated (ISO 8601).</summary>
    public string GeneratedUtc { get; init; } = string.Empty;

    /// <summary>The engine version that produced the statistics.</summary>
    public string EngineVersion { get; init; } = string.Empty;

    /// <summary>Top-level corpus counts.</summary>
    public CorpusCounts Corpus { get; init; } = new CorpusCounts();

    /// <summary>The classification thresholds used (so the categorization is reproducible).</summary>
    public ClassificationThresholds Thresholds { get; init; } = ClassificationThresholds.Default;

    /// <summary>Per-category counts and percentages (structural traits only).</summary>
    public IReadOnlyList<CategoryCount> Categories { get; init; } = Array.Empty<CategoryCount>();

    /// <summary>Aggregate image-resolution statistics.</summary>
    public DpiStatistics Dpi { get; init; } = new DpiStatistics();

    /// <summary>The percentage of processed documents carrying an invisible OCR text layer.</summary>
    public double OcrLayerPresentPercent { get; init; }

    /// <summary>The percentage of processed documents containing an interactive form.</summary>
    public double FormPresentPercent { get; init; }

    /// <summary>Aggregate encryption statistics.</summary>
    public EncryptionStatistics Encryption { get; init; } = new EncryptionStatistics();

    /// <summary>Per-reason rejection counts.</summary>
    public IReadOnlyList<RejectionCount> Rejections { get; init; } = Array.Empty<RejectionCount>();
}

/// <summary>Top-level corpus counts.</summary>
public sealed class CorpusCounts
{
    /// <summary>The total number of files discovered.</summary>
    public int Total { get; init; }

    /// <summary>The number of files that passed validation and were analyzed.</summary>
    public int Processed { get; init; }

    /// <summary>The number of files rejected by integrity validation.</summary>
    public int Rejected { get; init; }

    /// <summary>The number of encrypted files that could not be authenticated.</summary>
    public int EncryptedUnreadable { get; init; }
}

/// <summary>A single structural category's count and share of the corpus.</summary>
public sealed class CategoryCount
{
    /// <summary>The structural category name.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>The number of documents in the category.</summary>
    public int Count { get; init; }

    /// <summary>The category's percentage of the total corpus.</summary>
    public double Percent { get; init; }
}

/// <summary>Aggregate image-resolution statistics across all measured images.</summary>
public sealed class DpiStatistics
{
    /// <summary>The total number of images measured.</summary>
    public int ImagesMeasured { get; init; }

    /// <summary>The percentage of measured images below the flagging threshold.</summary>
    public double BelowThresholdPercent { get; init; }

    /// <summary>The DPI threshold images were compared against.</summary>
    public int Threshold { get; init; }

    /// <summary>The minimum effective DPI observed (0 when no images measured).</summary>
    public double Min { get; init; }

    /// <summary>The median effective DPI (nearest-rank; 0 when no images measured).</summary>
    public double Median { get; init; }

    /// <summary>The 95th-percentile effective DPI (nearest-rank; 0 when no images measured).</summary>
    public double P95 { get; init; }

    /// <summary>The effective-DPI distribution in fixed buckets.</summary>
    public IReadOnlyList<DpiBucket> Histogram { get; init; } = Array.Empty<DpiBucket>();
}

/// <summary>One bucket of the effective-DPI histogram.</summary>
public sealed class DpiBucket
{
    /// <summary>The bucket label (e.g. <c>&lt;150</c>, <c>150-299</c>).</summary>
    public string Bucket { get; init; } = string.Empty;

    /// <summary>The number of measured images in the bucket.</summary>
    public int Count { get; init; }
}

/// <summary>Aggregate encryption statistics.</summary>
public sealed class EncryptionStatistics
{
    /// <summary>The percentage of the corpus that is encrypted.</summary>
    public double EncryptedPercent { get; init; }

    /// <summary>Per-cipher counts among encrypted documents.</summary>
    public IReadOnlyList<AlgorithmCount> ByAlgorithm { get; init; } = Array.Empty<AlgorithmCount>();
}

/// <summary>A single cipher's count among encrypted documents.</summary>
public sealed class AlgorithmCount
{
    /// <summary>The cipher name (e.g. <c>Aes256</c>, <c>Rc4</c>).</summary>
    public string Algorithm { get; init; } = string.Empty;

    /// <summary>The number of documents using the cipher.</summary>
    public int Count { get; init; }
}

/// <summary>A single rejection reason's count.</summary>
public sealed class RejectionCount
{
    /// <summary>The machine-readable rejection reason.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>The number of documents rejected for the reason.</summary>
    public int Count { get; init; }
}
