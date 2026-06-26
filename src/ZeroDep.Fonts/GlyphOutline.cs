using System;
using System.Collections.Generic;

namespace ZeroDep.Fonts;

/// <summary>The kind of a glyph path segment.</summary>
public enum SegmentType
{
    /// <summary>A straight line to the end point.</summary>
    Line = 0,

    /// <summary>A quadratic Bézier (one control point) — TrueType.</summary>
    Quadratic = 1,

    /// <summary>A cubic Bézier (two control points) — CFF / Type 1.</summary>
    Cubic = 2,
}

/// <summary>
/// One segment of a glyph contour, in font units. The start point is the previous segment's end (or the
/// contour's start); this carries the control point(s) and the end point. Both quadratic (TrueType) and
/// cubic (CFF/Type 1) curves are representable so a single rasterizer can flatten any outline.
/// </summary>
public readonly struct GlyphSegment
{
    private GlyphSegment(SegmentType type, double c1x, double c1y, double c2x, double c2y, double ex, double ey)
    {
        Type = type;
        Control1X = c1x;
        Control1Y = c1y;
        Control2X = c2x;
        Control2Y = c2y;
        EndX = ex;
        EndY = ey;
    }

    /// <summary>The segment kind.</summary>
    public SegmentType Type { get; }

    /// <summary>First control point X (quadratic/cubic).</summary>
    public double Control1X { get; }

    /// <summary>First control point Y (quadratic/cubic).</summary>
    public double Control1Y { get; }

    /// <summary>Second control point X (cubic only).</summary>
    public double Control2X { get; }

    /// <summary>Second control point Y (cubic only).</summary>
    public double Control2Y { get; }

    /// <summary>End point X.</summary>
    public double EndX { get; }

    /// <summary>End point Y.</summary>
    public double EndY { get; }

    /// <summary>Creates a line segment.</summary>
    public static GlyphSegment Line(double x, double y) => new GlyphSegment(SegmentType.Line, 0, 0, 0, 0, x, y);

    /// <summary>Creates a quadratic Bézier segment.</summary>
    public static GlyphSegment Quadratic(double cx, double cy, double x, double y)
        => new GlyphSegment(SegmentType.Quadratic, cx, cy, 0, 0, x, y);

    /// <summary>Creates a cubic Bézier segment.</summary>
    public static GlyphSegment Cubic(double c1x, double c1y, double c2x, double c2y, double x, double y)
        => new GlyphSegment(SegmentType.Cubic, c1x, c1y, c2x, c2y, x, y);
}

/// <summary>A single closed contour: a start point and the ordered segments back around to it.</summary>
public sealed class GlyphContour
{
    /// <summary>Creates a contour.</summary>
    /// <param name="startX">The contour start X (font units).</param>
    /// <param name="startY">The contour start Y (font units).</param>
    /// <param name="segments">The segments, in order; the last implicitly closes to the start.</param>
    public GlyphContour(double startX, double startY, IReadOnlyList<GlyphSegment> segments)
    {
        StartX = startX;
        StartY = startY;
        Segments = segments ?? throw new ArgumentNullException(nameof(segments));
    }

    /// <summary>The contour's start point X.</summary>
    public double StartX { get; }

    /// <summary>The contour's start point Y.</summary>
    public double StartY { get; }

    /// <summary>The segments, in order.</summary>
    public IReadOnlyList<GlyphSegment> Segments { get; }
}

/// <summary>
/// A glyph's outline: its closed contours (in font units, em square = <c>UnitsPerEm</c>), advance width,
/// and bounding box. The common currency all font-program parsers produce and the rasterizer consumes.
/// </summary>
public sealed class GlyphOutline
{
    /// <summary>Creates a glyph outline.</summary>
    /// <param name="contours">The closed contours.</param>
    /// <param name="advanceWidth">The advance width (font units).</param>
    public GlyphOutline(IReadOnlyList<GlyphContour> contours, int advanceWidth)
    {
        Contours = contours ?? throw new ArgumentNullException(nameof(contours));
        AdvanceWidth = advanceWidth;
    }

    /// <summary>The glyph's closed contours.</summary>
    public IReadOnlyList<GlyphContour> Contours { get; }

    /// <summary>The advance width in font units.</summary>
    public int AdvanceWidth { get; }

    /// <summary>An empty outline (e.g. the space glyph or a missing glyph).</summary>
    public static GlyphOutline Empty { get; } = new GlyphOutline(Array.Empty<GlyphContour>(), 0);

    /// <summary>True when the outline has no contours.</summary>
    public bool IsEmpty => Contours.Count == 0;
}

/// <summary>Accumulates segments into contours (a moveto starts a new contour).</summary>
internal sealed class ContourBuilder
{
    private readonly List<GlyphContour> _contours = new List<GlyphContour>();
    private List<GlyphSegment>? _segments;
    private double _startX;
    private double _startY;

    public void MoveTo(double x, double y)
    {
        Flush();
        _segments = new List<GlyphSegment>();
        _startX = x;
        _startY = y;
    }

    public void LineTo(double x, double y) => _segments?.Add(GlyphSegment.Line(x, y));

    public void QuadTo(double cx, double cy, double x, double y) => _segments?.Add(GlyphSegment.Quadratic(cx, cy, x, y));

    public void CubicTo(double c1x, double c1y, double c2x, double c2y, double x, double y)
        => _segments?.Add(GlyphSegment.Cubic(c1x, c1y, c2x, c2y, x, y));

    public IReadOnlyList<GlyphContour> Build()
    {
        Flush();
        return _contours;
    }

    private void Flush()
    {
        if (_segments is { Count: > 0 })
        {
            _contours.Add(new GlyphContour(_startX, _startY, _segments));
        }

        _segments = null;
    }
}
