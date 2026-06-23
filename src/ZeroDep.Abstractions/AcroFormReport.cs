using System;
using System.Collections.Generic;

namespace ZeroDep.Abstractions;

/// <summary>The result of analyzing a PDF's interactive form (AcroForm).</summary>
public sealed class AcroFormReport
{
    /// <summary>Whether the document declares an <c>/AcroForm</c>.</summary>
    public bool HasAcroForm { get; init; }

    /// <summary>
    /// Whether the form is a dynamic XFA form (the <c>/AcroForm</c> carries an <c>/XFA</c> packet).
    /// For such documents the real content lives in the Adobe-only XFA stream and the visible page
    /// is typically just a "Please wait…" placeholder, so extracted page text should not be trusted
    /// as the form's content. ISO 32000-2 §12.7.8.
    /// </summary>
    public bool HasXfa { get; init; }

    /// <summary>The terminal form fields, in document order.</summary>
    public IReadOnlyList<FormFieldInfo> Fields { get; init; } = Array.Empty<FormFieldInfo>();
}
