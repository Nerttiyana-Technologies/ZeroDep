using System.Collections.Generic;

namespace ZeroDep.Content;

/// <summary>The output of interpreting a content stream: image placements and text runs.</summary>
internal sealed class ContentResult
{
    /// <summary>Images drawn on the page (and inside nested forms).</summary>
    public List<ImagePlacement> Images { get; } = new List<ImagePlacement>();

    /// <summary>Text runs shown on the page, in content-stream order.</summary>
    public List<TextRun> TextRuns { get; } = new List<TextRun>();
}
