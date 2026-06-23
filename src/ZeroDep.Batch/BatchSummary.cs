namespace ZeroDep.Batch;

/// <summary>The outcome of a corpus batch run.</summary>
public sealed class BatchSummary
{
    /// <summary>The total number of files discovered.</summary>
    public int Total { get; init; }

    /// <summary>The number of files processed in this run (excludes resumed/skipped files).</summary>
    public int ProcessedThisRun { get; init; }

    /// <summary>The number of files skipped because they were already complete (resume).</summary>
    public int Skipped { get; init; }

    /// <summary>The number of files rejected by integrity validation.</summary>
    public int Rejected { get; init; }

    /// <summary>The number of encrypted files that could not be authenticated.</summary>
    public int EncryptedUnreadable { get; init; }

    /// <summary>The path to the written aggregate statistics JSON.</summary>
    public string AggregatePath { get; init; } = string.Empty;

    /// <summary>The computed aggregate statistics.</summary>
    public AggregateStatistics Statistics { get; init; } = new AggregateStatistics();
}
