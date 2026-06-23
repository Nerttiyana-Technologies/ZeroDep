using System;
using ZeroDep.Abstractions;

namespace ZeroDep.Batch;

/// <summary>Configuration for a corpus batch run (ADR Feature G).</summary>
public sealed class BatchOptions
{
    /// <summary>The root directory scanned recursively for <c>*.pdf</c> files.</summary>
    public string InputDirectory { get; init; } = string.Empty;

    /// <summary>
    /// The directory where the resumable ledger, the publishable aggregate statistics, and (when
    /// enabled) the per-file verification JSON are written.
    /// </summary>
    public string OutputDirectory { get; init; } = string.Empty;

    /// <summary>The maximum number of files processed concurrently. Defaults to the processor count.</summary>
    public int MaxConcurrency { get; init; } = Environment.ProcessorCount;

    /// <summary>The effective-DPI threshold below which images are flagged.</summary>
    public int DpiThreshold { get; init; } = AnalyzerOptions.DefaultDpiThreshold;

    /// <summary>An optional password applied to encrypted documents (empty/default when null).</summary>
    public string? Password { get; init; }

    /// <summary>
    /// When true, files already recorded in the ledger with a matching input hash are skipped, so an
    /// interrupted run resumes where it left off.
    /// </summary>
    public bool Resume { get; init; } = true;

    /// <summary>
    /// Whether to write the full per-file §8.1 verification JSON for each processed file (keyed by
    /// its anonymized id). This output is internal and never part of the published artifact.
    /// </summary>
    public bool WritePerFileJson { get; init; } = true;

    /// <summary>The structural-classification thresholds.</summary>
    public ClassificationThresholds Thresholds { get; init; } = ClassificationThresholds.Default;
}
