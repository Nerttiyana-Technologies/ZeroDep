namespace ZeroDep.Abstractions;

/// <summary>
/// A single page's structural classification (ADR-0003): its <see cref="Class"/>, a 0–1
/// <see cref="Confidence"/> (the sole carrier of "uncertain" — low confidence means escalate), and the
/// <see cref="PageSignals"/> behind it. Additive; the document-level category is a roll-up of these.
/// </summary>
public sealed class PageClassification
{
    /// <summary>The zero-based page index.</summary>
    public int PageIndex { get; init; }

    /// <summary>The assigned structural class.</summary>
    public PageContentClass Class { get; init; }

    /// <summary>Confidence in the assigned class, 0–1. Low confidence signals "uncertain — escalate".</summary>
    public double Confidence { get; init; }

    /// <summary>The deterministic signals the class was derived from.</summary>
    public PageSignals Signals { get; init; } = new PageSignals();
}
