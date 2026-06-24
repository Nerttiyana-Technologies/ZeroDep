using System;
using System.Collections.Generic;

namespace ZeroDep.Ocr;

/// <summary>Options controlling a single OCR recognition run.</summary>
public sealed class OcrOptions
{
    /// <summary>
    /// The languages to recognize, as engine-specific codes (for example <c>eng</c>). An empty list
    /// lets the engine choose its default.
    /// </summary>
    public IReadOnlyList<string> Languages { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Recognized lines below this confidence (0–1) are dropped. The default of <c>0</c> keeps every
    /// line the engine returns.
    /// </summary>
    public double MinConfidence { get; init; }
}
