using System.Collections.Generic;

namespace ZeroDep.Content;

/// <summary>The output of interpreting a content stream: image placements, text runs, and structural signals.</summary>
internal sealed class ContentResult
{
    /// <summary>Images drawn on the page (and inside nested forms).</summary>
    public List<ImagePlacement> Images { get; } = new List<ImagePlacement>();

    /// <summary>Text runs shown on the page, in content-stream order.</summary>
    public List<TextRun> TextRuns { get; } = new List<TextRun>();

    /// <summary>Count of axis-aligned vector ruling lines (long, thin segments from path operators).</summary>
    public int RulingLineCount { get; set; }

    /// <summary>Distinct font resource names selected (via <c>Tf</c>) — for the font-distinct signal.</summary>
    public HashSet<string> FontNames { get; } = new HashSet<string>(System.StringComparer.Ordinal);
}
