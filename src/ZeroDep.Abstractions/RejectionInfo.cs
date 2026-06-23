namespace ZeroDep.Abstractions;

/// <summary>Why a document was rejected by integrity validation.</summary>
public sealed class RejectionInfo
{
    /// <summary>The machine-readable rejection reason.</summary>
    public RejectionReason Reason { get; init; }

    /// <summary>An optional human-readable detail.</summary>
    public string? Detail { get; init; }
}
