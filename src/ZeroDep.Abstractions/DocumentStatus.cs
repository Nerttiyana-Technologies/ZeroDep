namespace ZeroDep.Abstractions;

/// <summary>Outcome of attempting to analyze a PDF document.</summary>
public enum DocumentStatus
{
    /// <summary>The document passed validation and was analyzed.</summary>
    Processed = 0,

    /// <summary>The document failed integrity validation and was rejected (see <see cref="RejectionReason"/>).</summary>
    Rejected = 1,
}
