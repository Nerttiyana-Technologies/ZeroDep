using System;
using System.Collections.Generic;
using ZeroDep.Abstractions;

namespace ZeroDep.Ocr;

/// <summary>The result of an OCR recognition run.</summary>
public sealed class OcrResult
{
    /// <summary>The recognized lines, in reading order where the engine provides one.</summary>
    public IReadOnlyList<OcrLine> Lines { get; init; } = Array.Empty<OcrLine>();
}

/// <summary>A single recognized line of text with its position and confidence.</summary>
public sealed class OcrLine
{
    /// <summary>The recognized text.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Recognition confidence in the range 0–1.</summary>
    public double Confidence { get; init; }

    /// <summary>The line's bounding box, in image pixel coordinates.</summary>
    public BoundingBox Bounds { get; init; }
}
