namespace ZeroDep.Abstractions;

/// <summary>A single interactive form field extracted from a PDF's AcroForm (Feature C).</summary>
public sealed class FormFieldInfo
{
    /// <summary>The fully-qualified field name (partial names joined down the parent chain with '.').</summary>
    public string FullyQualifiedName { get; init; } = string.Empty;

    /// <summary>The field's own partial name (<c>/T</c>), or null.</summary>
    public string? PartialName { get; init; }

    /// <summary>The human-readable label (<c>/TU</c>), or null.</summary>
    public string? Label { get; init; }

    /// <summary>The field type: <c>Tx</c>, <c>Ch</c>, <c>Btn</c>, <c>Sig</c>, or empty if unknown.</summary>
    public string FieldType { get; init; } = string.Empty;

    /// <summary>The field value as text (for buttons, the selected/appearance state name), or null.</summary>
    public string? Value { get; init; }

    /// <summary>For button fields (checkbox/radio): whether it is on; null for non-button fields.</summary>
    public bool? IsChecked { get; init; }

    /// <summary>The zero-based page index the field's widget appears on, or null if not associated.</summary>
    public int? PageIndex { get; init; }

    /// <summary>The widget bounding box (/Rect) in PDF device space, where known.</summary>
    public BoundingBox? Rect { get; init; }
}
