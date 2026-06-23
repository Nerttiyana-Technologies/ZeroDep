namespace ZeroDep.Abstractions;

/// <summary>
/// One atomic extracted value in the coverage manifest. The manifest is the exact set of values
/// the engine found; a consumer can verify every item appears in its output ("nothing lost").
/// </summary>
public sealed class CoverageItem
{
    /// <summary>Stable id within the document (e.g. <c>t12</c>, <c>f3</c>).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The value kind: <c>text</c>, <c>field</c>, or <c>checkbox</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>The atomic value (run text, field value, or checkbox state).</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>The page index the value belongs to, or -1 if not page-associated.</summary>
    public int Page { get; init; }

    /// <summary>The value's bounding box, where known.</summary>
    public BoundingBox Bounds { get; init; }
}
