using System;
using System.Collections.Generic;

namespace ZeroDep.Abstractions;

/// <summary>The complete structural analysis of a PDF: images, text, form, and coverage manifest.</summary>
public sealed class DocumentAnalysis
{
    /// <summary>
    /// The output schema version. <c>1.1</c> adds the per-page <see cref="Pages"/> classification; <c>1.2</c>
    /// adds <c>signals.textDecodeConfidence</c> (the per-page text-decode trust signal, ADR-0007).
    /// </summary>
    public string SchemaVersion { get; init; } = "1.2";

    /// <summary>Whether the document was processed or rejected.</summary>
    public DocumentStatus Status { get; init; } = DocumentStatus.Processed;

    /// <summary>Rejection detail when <see cref="Status"/> is <see cref="DocumentStatus.Rejected"/>; otherwise null.</summary>
    public RejectionInfo? Rejection { get; init; }

    /// <summary>The number of pages.</summary>
    public int PageCount { get; init; }

    /// <summary>Encryption / access-control status (Feature E).</summary>
    public SecurityInfo Security { get; init; } = new SecurityInfo();

    /// <summary>Per-image DPI metrics (Feature A).</summary>
    public IReadOnlyList<ImageDpiInfo> Images { get; init; } = Array.Empty<ImageDpiInfo>();

    /// <summary>Positioned text runs (Feature B).</summary>
    public IReadOnlyList<TextRunInfo> TextRuns { get; init; } = Array.Empty<TextRunInfo>();

    /// <summary>Interactive form fields (Feature C).</summary>
    public AcroFormReport Form { get; init; } = new AcroFormReport();

    /// <summary>The coverage manifest — the exact set of atomic values extracted.</summary>
    public IReadOnlyList<CoverageItem> Coverage { get; init; } = Array.Empty<CoverageItem>();

    /// <summary>Per-page structural classification with signals (ADR-0003); one entry per page in order.</summary>
    public IReadOnlyList<PageClassification> Pages { get; init; } = Array.Empty<PageClassification>();

    /// <summary>
    /// The maximum fraction (0–1) of any single page's area covered by raster images. This is a
    /// content-free, structural signal used to classify "image-dominated" (scanned) documents.
    /// </summary>
    public double ImageAreaFraction { get; init; }
}
