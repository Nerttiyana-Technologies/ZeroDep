using System;
using System.Collections.Generic;
using ZeroDep.Fonts;

namespace ZeroDep.Raster;

/// <summary>
/// Rasterizes a <see cref="GlyphOutline"/> to an anti-aliased 8-bit coverage <see cref="GlyphBitmap"/>
/// (ADR-0005 §2.3). Béziers (quadratic and cubic) are flattened to line segments at an adaptive tolerance,
/// then filled with the non-zero winding rule using vertical supersampling and exact horizontal coverage —
/// deterministic by construction.
/// </summary>
public static class GlyphRasterizer
{
    private const double FlatTolerance = 0.2;   // device pixels
    private const int MaxSubdivision = 18;

    /// <summary>Renders a glyph outline at the given scale (pixels per font unit).</summary>
    /// <param name="outline">The glyph outline (font units).</param>
    /// <param name="scale">Pixels per font unit (e.g. pixelSize / unitsPerEm).</param>
    /// <param name="subSamples">Vertical supersampling factor (coverage levels for horizontal edges).</param>
    public static GlyphBitmap Render(GlyphOutline outline, double scale, int subSamples = 8)
    {
        if (outline is null)
        {
            throw new ArgumentNullException(nameof(outline));
        }

        if (outline.IsEmpty || scale <= 0)
        {
            return GlyphBitmap.Empty;
        }

        int n = subSamples < 1 ? 1 : subSamples;

        // Flatten every contour to device-space polylines (y-down, baseline at y = 0).
        var contours = new List<List<double>>(); // each: x0,y0,x1,y1,... (flattened points)
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (GlyphContour contour in outline.Contours)
        {
            var pts = new List<double>();
            double cx = contour.StartX * scale;
            double cy = -contour.StartY * scale;
            Add(pts, cx, cy, ref minX, ref minY, ref maxX, ref maxY);

            foreach (GlyphSegment seg in contour.Segments)
            {
                double ex = seg.EndX * scale;
                double ey = -seg.EndY * scale;
                switch (seg.Type)
                {
                    case SegmentType.Line:
                        Add(pts, ex, ey, ref minX, ref minY, ref maxX, ref maxY);
                        break;
                    case SegmentType.Quadratic:
                        FlattenQuad(pts, cx, cy, seg.Control1X * scale, -seg.Control1Y * scale, ex, ey, 0, ref minX, ref minY, ref maxX, ref maxY);
                        break;
                    default:
                        FlattenCubic(pts, cx, cy, seg.Control1X * scale, -seg.Control1Y * scale, seg.Control2X * scale, -seg.Control2Y * scale, ex, ey, 0, ref minX, ref minY, ref maxX, ref maxY);
                        break;
                }

                cx = ex;
                cy = ey;
            }

            contours.Add(pts);
        }

        if (minX > maxX || minY > maxY)
        {
            return GlyphBitmap.Empty;
        }

        int originX = (int)Math.Floor(minX);
        int originY = (int)Math.Floor(minY);
        int width = (int)Math.Ceiling(maxX) - originX;
        int height = (int)Math.Ceiling(maxY) - originY;
        if (width <= 0 || height <= 0)
        {
            return GlyphBitmap.Empty;
        }

        // Build edges in bitmap-local coordinates.
        var edges = new List<Edge>();
        foreach (List<double> pts in contours)
        {
            int count = pts.Count / 2;
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                double x0 = pts[i * 2] - originX, y0 = pts[(i * 2) + 1] - originY;
                double x1 = pts[j * 2] - originX, y1 = pts[(j * 2) + 1] - originY;
                if (y0 != y1)
                {
                    edges.Add(new Edge(x0, y0, x1, y1));
                }
            }
        }

        var coverage = new float[width * height];
        double weight = 1.0 / n;
        var crossings = new List<(double X, int Dir)>(16);

        for (int y = 0; y < height; y++)
        {
            int rowBase = y * width;
            for (int k = 0; k < n; k++)
            {
                double sy = y + ((k + 0.5) / n);
                crossings.Clear();
                foreach (Edge e in edges)
                {
                    if (sy < e.YLo || sy >= e.YHi)
                    {
                        continue;
                    }

                    double t = (sy - e.YLo) / (e.YHi - e.YLo);
                    crossings.Add((e.XLo + (t * (e.XHi - e.XLo)), e.Dir));
                }

                if (crossings.Count < 2)
                {
                    continue;
                }

                crossings.Sort((a, b) => a.X.CompareTo(b.X));

                int wind = 0;
                double spanStart = 0;
                foreach ((double X, int Dir) c in crossings)
                {
                    int newWind = wind + c.Dir;
                    if (wind == 0 && newWind != 0)
                    {
                        spanStart = c.X;
                    }
                    else if (wind != 0 && newWind == 0)
                    {
                        AddSpan(coverage, rowBase, spanStart, c.X, width, (float)weight);
                    }

                    wind = newWind;
                }
            }
        }

        var bytes = new byte[width * height];
        for (int i = 0; i < bytes.Length; i++)
        {
            double v = coverage[i];
            v = v < 0 ? 0 : (v > 1 ? 1 : v);
            bytes[i] = (byte)Math.Round(v * 255.0, MidpointRounding.AwayFromZero);
        }

        return new GlyphBitmap(width, height, originX, -originY, bytes);
    }

    private static void AddSpan(float[] coverage, int rowBase, double xs, double xe, int width, float weight)
    {
        if (xs < 0)
        {
            xs = 0;
        }

        if (xe > width)
        {
            xe = width;
        }

        if (xe <= xs)
        {
            return;
        }

        int ixs = (int)Math.Floor(xs);
        int ixe = (int)Math.Floor(xe);
        if (ixs == ixe)
        {
            coverage[rowBase + ixs] += weight * (float)(xe - xs);
            return;
        }

        coverage[rowBase + ixs] += weight * (float)(ixs + 1 - xs);
        for (int p = ixs + 1; p < ixe; p++)
        {
            coverage[rowBase + p] += weight;
        }

        if (ixe < width)
        {
            coverage[rowBase + ixe] += weight * (float)(xe - ixe);
        }
    }

    private static void Add(List<double> pts, double x, double y, ref double minX, ref double minY, ref double maxX, ref double maxY)
    {
        pts.Add(x);
        pts.Add(y);
        if (x < minX) minX = x;
        if (x > maxX) maxX = x;
        if (y < minY) minY = y;
        if (y > maxY) maxY = y;
    }

    private static void FlattenQuad(List<double> pts, double x0, double y0, double cx, double cy, double x1, double y1, int depth, ref double minX, ref double minY, ref double maxX, ref double maxY)
    {
        // Flatness: distance of the control point from the chord.
        double d = PointLineDistance(cx, cy, x0, y0, x1, y1);
        if (depth >= MaxSubdivision || d <= FlatTolerance)
        {
            Add(pts, x1, y1, ref minX, ref minY, ref maxX, ref maxY);
            return;
        }

        double m01x = (x0 + cx) / 2, m01y = (y0 + cy) / 2;
        double m12x = (cx + x1) / 2, m12y = (cy + y1) / 2;
        double mx = (m01x + m12x) / 2, my = (m01y + m12y) / 2;
        FlattenQuad(pts, x0, y0, m01x, m01y, mx, my, depth + 1, ref minX, ref minY, ref maxX, ref maxY);
        FlattenQuad(pts, mx, my, m12x, m12y, x1, y1, depth + 1, ref minX, ref minY, ref maxX, ref maxY);
    }

    private static void FlattenCubic(List<double> pts, double x0, double y0, double c1x, double c1y, double c2x, double c2y, double x1, double y1, int depth, ref double minX, ref double minY, ref double maxX, ref double maxY)
    {
        double d = PointLineDistance(c1x, c1y, x0, y0, x1, y1) + PointLineDistance(c2x, c2y, x0, y0, x1, y1);
        if (depth >= MaxSubdivision || d <= FlatTolerance)
        {
            Add(pts, x1, y1, ref minX, ref minY, ref maxX, ref maxY);
            return;
        }

        double m01x = (x0 + c1x) / 2, m01y = (y0 + c1y) / 2;
        double m12x = (c1x + c2x) / 2, m12y = (c1y + c2y) / 2;
        double m23x = (c2x + x1) / 2, m23y = (c2y + y1) / 2;
        double ax = (m01x + m12x) / 2, ay = (m01y + m12y) / 2;
        double bx = (m12x + m23x) / 2, by = (m12y + m23y) / 2;
        double mx = (ax + bx) / 2, my = (ay + by) / 2;
        FlattenCubic(pts, x0, y0, m01x, m01y, ax, ay, mx, my, depth + 1, ref minX, ref minY, ref maxX, ref maxY);
        FlattenCubic(pts, mx, my, bx, by, m23x, m23y, x1, y1, depth + 1, ref minX, ref minY, ref maxX, ref maxY);
    }

    private static double PointLineDistance(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay;
        double len = Math.Sqrt((dx * dx) + (dy * dy));
        if (len < 1e-9)
        {
            double ex = px - ax, ey = py - ay;
            return Math.Sqrt((ex * ex) + (ey * ey));
        }

        return Math.Abs(((px - ax) * dy) - ((py - ay) * dx)) / len;
    }

    private readonly struct Edge
    {
        public Edge(double x0, double y0, double x1, double y1)
        {
            if (y0 < y1)
            {
                YLo = y0;
                YHi = y1;
                XLo = x0;
                XHi = x1;
                Dir = 1;
            }
            else
            {
                YLo = y1;
                YHi = y0;
                XLo = x1;
                XHi = x0;
                Dir = -1;
            }
        }

        public double YLo { get; }

        public double YHi { get; }

        public double XLo { get; }

        public double XHi { get; }

        public int Dir { get; }
    }
}
