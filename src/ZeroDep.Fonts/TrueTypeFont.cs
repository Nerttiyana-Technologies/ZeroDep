using System;
using System.Collections.Generic;

namespace ZeroDep.Fonts;

/// <summary>
/// Pure-BCL parser for an embedded TrueType (SFNT, <c>glyf</c>) font program (PDF <c>/FontFile2</c>,
/// ISO/IEC 14496-22). Parses the table directory, <c>head</c>/<c>maxp</c>/<c>loca</c>/<c>glyf</c>/<c>hmtx</c>
/// and <c>cmap</c>, and produces a <see cref="GlyphOutline"/> per glyph id (simple and composite glyphs,
/// quadratic Béziers). Malformed glyphs degrade to empty outlines rather than throwing.
/// </summary>
public sealed class TrueTypeFont
{
    private const int MaxCompositeDepth = 8;

    private readonly byte[] _data;
    private readonly int[] _loca;
    private readonly int[] _advanceWidths;
    private readonly int[] _leftSideBearings;
    private readonly int _glyfOffset;
    private readonly CmapTable? _cmap;
    private readonly byte[] _fpgm;
    private readonly byte[] _prep;
    private readonly byte[] _cvt;
    private readonly HintLimits _limits;
    private TrueTypeHinter? _hinter;
    private int _hinterPpem;

    private TrueTypeFont(byte[] data, int unitsPerEm, int numGlyphs, int[] loca, int[] advanceWidths, int[] leftSideBearings, int glyfOffset, CmapTable? cmap, byte[] fpgm, byte[] prep, byte[] cvt, HintLimits limits)
    {
        _data = data;
        UnitsPerEm = unitsPerEm;
        GlyphCount = numGlyphs;
        _loca = loca;
        _advanceWidths = advanceWidths;
        _leftSideBearings = leftSideBearings;
        _glyfOffset = glyfOffset;
        _cmap = cmap;
        _fpgm = fpgm;
        _prep = prep;
        _cvt = cvt;
        _limits = limits;
    }

    /// <summary>True if the font carries hinting programs (<c>fpgm</c>/<c>prep</c>/glyph instructions).</summary>
    public bool HasHinting => _fpgm.Length > 0 || _prep.Length > 0;

    internal readonly struct HintLimits
    {
        public HintLimits(int maxStack, int maxStorage, int maxFunctionDefs, int maxInstructionDefs, int maxTwilightPoints)
        {
            MaxStack = Math.Max(maxStack, 64);
            MaxStorage = Math.Max(maxStorage, 1);
            MaxFunctionDefs = Math.Max(maxFunctionDefs, 1);
            MaxInstructionDefs = Math.Max(maxInstructionDefs, 0);
            MaxTwilightPoints = Math.Max(maxTwilightPoints, 1);
        }

        public int MaxStack { get; }

        public int MaxStorage { get; }

        public int MaxFunctionDefs { get; }

        public int MaxInstructionDefs { get; }

        public int MaxTwilightPoints { get; }
    }

    /// <summary>The em square size in font units (e.g. 1000 or 2048).</summary>
    public int UnitsPerEm { get; }

    /// <summary>The number of glyphs.</summary>
    public int GlyphCount { get; }

    /// <summary>
    /// Parses an embedded TrueType font program. Throws <see cref="NotSupportedException"/> for a
    /// CFF-flavoured OpenType ('OTTO') program — that is handled by the CFF parser.
    /// </summary>
    /// <param name="data">The decoded <c>/FontFile2</c> bytes.</param>
    public static TrueTypeFont Load(byte[] data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        var r = new BigEndianReader(data);
        uint version = r.ReadU32();
        if (version == 0x4F54544F)
        {
            throw new NotSupportedException("OpenType/CFF ('OTTO') font — use the CFF parser.");
        }

        int numTables = r.ReadU16();
        r.Position = 12;
        var tables = new Dictionary<string, (int Offset, int Length)>(StringComparer.Ordinal);
        for (int i = 0; i < numTables; i++)
        {
            string tag = ReadTag(r);
            r.ReadU32(); // checksum
            int offset = (int)r.ReadU32();
            int length = (int)r.ReadU32();
            tables[tag] = (offset, length);
        }

        if (!tables.TryGetValue("head", out (int Offset, int Length) head)
            || !tables.TryGetValue("maxp", out (int Offset, int Length) maxp)
            || !tables.TryGetValue("loca", out (int Offset, int Length) locaTab)
            || !tables.TryGetValue("glyf", out (int Offset, int Length) glyf))
        {
            throw new NotSupportedException("Not a glyf-based TrueType font (missing required tables).");
        }

        r.Position = head.Offset + 18;
        int unitsPerEm = r.ReadU16();
        r.Position = head.Offset + 50;
        int indexToLocFormat = r.ReadS16();

        r.Position = maxp.Offset + 4;
        int numGlyphs = r.ReadU16();
        var limits = new HintLimits(0, 0, 0, 0, 0);
        if (maxp.Length >= 32)
        {
            r.Position = maxp.Offset + 18;
            int maxStorage = r.ReadU16();
            int maxFunctionDefs = r.ReadU16();
            int maxInstructionDefs = r.ReadU16();
            int maxStack = r.ReadU16();
            r.ReadU16(); // maxSizeOfInstructions
            r.ReadU16(); // maxComponentElements
            r.ReadU16(); // maxComponentDepth (skip back below)
            r.Position = maxp.Offset + 16;
            int maxTwilight = r.ReadU16();
            limits = new HintLimits(maxStack, maxStorage, maxFunctionDefs, maxInstructionDefs, maxTwilight);
        }

        int numberOfHMetrics = 0;
        if (tables.TryGetValue("hhea", out (int Offset, int Length) hhea))
        {
            r.Position = hhea.Offset + 34;
            numberOfHMetrics = r.ReadU16();
        }

        int[] loca = ReadLoca(r, locaTab.Offset, numGlyphs, indexToLocFormat);
        (int[] advances, int[] lsbs) = ReadHmtx(r, tables, numberOfHMetrics, numGlyphs);
        CmapTable? cmap = tables.TryGetValue("cmap", out (int Offset, int Length) cm) ? CmapTable.Parse(data, cm.Offset) : null;

        byte[] fpgm = Slice(data, tables, "fpgm");
        byte[] prep = Slice(data, tables, "prep");
        byte[] cvt = Slice(data, tables, "cvt ");

        return new TrueTypeFont(data, unitsPerEm, numGlyphs, loca, advances, lsbs, glyf.Offset, cmap, fpgm, prep, cvt, limits);
    }

    private static byte[] Slice(byte[] data, Dictionary<string, (int Offset, int Length)> tables, string tag)
    {
        if (!tables.TryGetValue(tag, out (int Offset, int Length) t) || t.Length <= 0 || t.Offset < 0 || t.Offset + t.Length > data.Length)
        {
            return Array.Empty<byte>();
        }

        var b = new byte[t.Length];
        Array.Copy(data, t.Offset, b, 0, t.Length);
        return b;
    }

    /// <summary>The advance width of a glyph in font units.</summary>
    /// <param name="glyphId">The glyph id.</param>
    public int GetAdvanceWidth(int glyphId)
        => glyphId >= 0 && glyphId < _advanceWidths.Length ? _advanceWidths[glyphId] : 0;

    /// <summary>Maps a Unicode code point to a glyph id via <c>cmap</c>, or 0 (.notdef) if unmapped.</summary>
    /// <param name="codepoint">The Unicode code point.</param>
    public int MapCodepointToGlyph(int codepoint) => _cmap?.Lookup(codepoint) ?? 0;

    /// <summary>Returns the outline of a glyph (font units), or <see cref="GlyphOutline.Empty"/> if absent.</summary>
    /// <param name="glyphId">The glyph id.</param>
    public GlyphOutline GetGlyph(int glyphId)
    {
        var builder = new ContourBuilder();
        AppendGlyph(glyphId, builder, 0);
        IReadOnlyList<GlyphContour> contours = builder.Build();

        // Align the outline's left edge to the metric left side bearing (FreeType convention): shift x by
        // (lsb - xMin). A no-op for well-formed fonts where xMin == lsb.
        if (contours.Count > 0)
        {
            int shift = GetLeftSideBearing(glyphId) - GlyphXMin(glyphId);
            if (shift != 0)
            {
                contours = ShiftX(contours, shift);
            }
        }

        return new GlyphOutline(contours, GetAdvanceWidth(glyphId));
    }

    /// <summary>
    /// Returns the hinted outline of a <b>simple</b> glyph at a given pixel-per-em size, with coordinates in
    /// 26.6 fixed-point device pixels (font units × 64 / unitsPerEm, then grid-fitted by the bytecode). Falls
    /// back to the linearly scaled (unhinted) outline for composite glyphs, hinting-free fonts, or on any
    /// interpreter fault. The font must carry <c>fpgm</c>/<c>prep</c> for hinting to take effect.
    /// </summary>
    /// <param name="glyphId">The glyph id.</param>
    /// <param name="pixelsPerEm">The target em size in pixels (ppem).</param>
    public GlyphOutline GetHintedGlyph(int glyphId, int pixelsPerEm)
    {
        if (pixelsPerEm <= 0)
        {
            return GlyphOutline.Empty;
        }

        RawGlyph? raw = ReadSimpleGlyphRaw(glyphId);
        if (raw is null || !HasHinting)
        {
            return ScaleOutline(GetGlyph(glyphId), pixelsPerEm);
        }

        try
        {
            if (_hinter is null || _hinterPpem != pixelsPerEm)
            {
                _hinter = new TrueTypeHinter(_cvt, _fpgm, _prep, UnitsPerEm, pixelsPerEm, _limits);
                _hinterPpem = pixelsPerEm;
            }

            int advance = GetAdvanceWidth(glyphId);
            int lsb = GetLeftSideBearing(glyphId);
            if (_hinter.TryHintSimpleGlyph(raw, advance, lsb, out int[] hx, out int[] hy))
            {
                return BuildHintedOutline(raw, hx, hy, ScaleAdvance(advance, pixelsPerEm));
            }
        }
        catch (Exception)
        {
            // hinting fault → fall back to the scaled unhinted outline
        }

        return ScaleOutline(GetGlyph(glyphId), pixelsPerEm);
    }

    // Returns the raw hinted point coordinates (26.6) of a simple glyph, in original point order, for
    // validation against a reference interpreter. False for composite/empty/unhinted glyphs.
    internal bool TryGetHintedPoints(int glyphId, int pixelsPerEm, out int[] x, out int[] y, out RawGlyph? raw)
    {
        x = Array.Empty<int>();
        y = Array.Empty<int>();
        raw = null;
        if (pixelsPerEm <= 0 || !HasHinting)
        {
            return false;
        }

        raw = ReadSimpleGlyphRaw(glyphId);
        if (raw is null || raw.Instructions.Length == 0)
        {
            return false;
        }

        if (_hinter is null || _hinterPpem != pixelsPerEm)
        {
            _hinter = new TrueTypeHinter(_cvt, _fpgm, _prep, UnitsPerEm, pixelsPerEm, _limits);
            _hinterPpem = pixelsPerEm;
        }

        return _hinter.TryHintSimpleGlyph(raw, GetAdvanceWidth(glyphId), GetLeftSideBearing(glyphId), out x, out y);
    }

    private int ScaleAdvance(int advance, int ppem)
        => (int)Math.Round((double)advance * ppem * 64 / UnitsPerEm);

    private GlyphOutline ScaleOutline(GlyphOutline outline, int ppem)
    {
        if (outline.IsEmpty)
        {
            return outline;
        }

        double s = (double)ppem * 64 / UnitsPerEm; // font units -> 26.6 px
        var contours = new List<GlyphContour>(outline.Contours.Count);
        foreach (GlyphContour c in outline.Contours)
        {
            var segs = new List<GlyphSegment>(c.Segments.Count);
            foreach (GlyphSegment seg in c.Segments)
            {
                segs.Add(seg.Type switch
                {
                    SegmentType.Line => GlyphSegment.Line(seg.EndX * s, seg.EndY * s),
                    SegmentType.Quadratic => GlyphSegment.Quadratic(seg.Control1X * s, seg.Control1Y * s, seg.EndX * s, seg.EndY * s),
                    _ => GlyphSegment.Cubic(seg.Control1X * s, seg.Control1Y * s, seg.Control2X * s, seg.Control2Y * s, seg.EndX * s, seg.EndY * s),
                });
            }

            contours.Add(new GlyphContour(c.StartX * s, c.StartY * s, segs));
        }

        return new GlyphOutline(contours, ScaleAdvance(outline.AdvanceWidth, ppem));
    }

    private static GlyphOutline BuildHintedOutline(RawGlyph raw, int[] hx, int[] hy, int advance)
    {
        var builder = new ContourBuilder();
        int start = 0;
        foreach (int end in raw.ContourEnds)
        {
            EmitContourInt(hx, hy, raw.OnCurve, start, end, builder);
            start = end + 1;
        }

        return new GlyphOutline(builder.Build(), advance);
    }

    // Integer variant of EmitContour operating on hinted 26.6 points.
    private static void EmitContourInt(int[] xs, int[] ys, bool[] on, int from, int to, ContourBuilder builder)
    {
        int n = to - from + 1;
        if (n < 1)
        {
            return;
        }

        bool On(int i) => on[from + i];
        double Px(int i) => xs[from + i];
        double Py(int i) => ys[from + i];

        double startX;
        double startY;
        int begin;
        int last;
        if (On(0))
        {
            startX = Px(0);
            startY = Py(0);
            begin = 1;
            last = n - 1;
        }
        else if (On(n - 1))
        {
            startX = Px(n - 1);
            startY = Py(n - 1);
            begin = 0;
            last = n - 2;
        }
        else
        {
            startX = (Px(0) + Px(n - 1)) / 2.0;
            startY = (Py(0) + Py(n - 1)) / 2.0;
            begin = 0;
            last = n - 1;
        }

        var seqX = new List<double>(n + 1);
        var seqY = new List<double>(n + 1);
        var seqOn = new List<bool>(n + 1);
        for (int i = begin; i <= last; i++)
        {
            seqX.Add(Px(i));
            seqY.Add(Py(i));
            seqOn.Add(On(i));
        }

        seqX.Add(startX);
        seqY.Add(startY);
        seqOn.Add(true);

        builder.MoveTo(startX, startY);
        int idx = 0;
        while (idx < seqX.Count)
        {
            if (seqOn[idx])
            {
                builder.LineTo(seqX[idx], seqY[idx]);
                idx++;
            }
            else
            {
                double cx = seqX[idx];
                double cy = seqY[idx];
                double ex;
                double ey;
                if (idx + 1 < seqX.Count && seqOn[idx + 1])
                {
                    ex = seqX[idx + 1];
                    ey = seqY[idx + 1];
                    idx += 2;
                }
                else if (idx + 1 < seqX.Count)
                {
                    ex = (cx + seqX[idx + 1]) / 2.0;
                    ey = (cy + seqY[idx + 1]) / 2.0;
                    idx += 1;
                }
                else
                {
                    ex = startX;
                    ey = startY;
                    idx += 1;
                }

                builder.QuadTo(cx, cy, ex, ey);
            }
        }
    }

    // Extracts the raw simple-glyph point data (no left-side-bearing alignment); null for composite/empty.
    internal RawGlyph? ReadSimpleGlyphRaw(int glyphId)
    {
        if (glyphId < 0 || glyphId + 1 >= _loca.Length)
        {
            return null;
        }

        int start = _glyfOffset + _loca[glyphId];
        int end = _glyfOffset + _loca[glyphId + 1];
        if (end <= start)
        {
            return null;
        }

        var r = new BigEndianReader(_data) { Position = start };
        int numberOfContours = r.ReadS16();
        if (numberOfContours < 0)
        {
            return null; // composite
        }

        r.Position += 8;
        if (numberOfContours == 0)
        {
            return null;
        }

        var endPts = new int[numberOfContours];
        for (int i = 0; i < numberOfContours; i++)
        {
            endPts[i] = r.ReadU16();
        }

        int numPoints = endPts[numberOfContours - 1] + 1;
        if (numPoints <= 0)
        {
            return null;
        }

        int instructionLength = r.ReadU16();
        var instructions = new byte[instructionLength];
        for (int i = 0; i < instructionLength; i++)
        {
            instructions[i] = r.ReadU8();
        }

        var flags = new byte[numPoints];
        for (int i = 0; i < numPoints;)
        {
            byte f = r.ReadU8();
            flags[i++] = f;
            if ((f & 0x08) != 0)
            {
                int repeat = r.ReadU8();
                for (int k = 0; k < repeat && i < numPoints; k++)
                {
                    flags[i++] = f;
                }
            }
        }

        var xs = new int[numPoints];
        int x = 0;
        for (int i = 0; i < numPoints; i++)
        {
            byte f = flags[i];
            if ((f & 0x02) != 0)
            {
                int dx = r.ReadU8();
                x += (f & 0x10) != 0 ? dx : -dx;
            }
            else if ((f & 0x10) == 0)
            {
                x += r.ReadS16();
            }

            xs[i] = x;
        }

        var ys = new int[numPoints];
        int y = 0;
        for (int i = 0; i < numPoints; i++)
        {
            byte f = flags[i];
            if ((f & 0x04) != 0)
            {
                int dy = r.ReadU8();
                y += (f & 0x20) != 0 ? dy : -dy;
            }
            else if ((f & 0x20) == 0)
            {
                y += r.ReadS16();
            }

            ys[i] = y;
        }

        var on = new bool[numPoints];
        for (int i = 0; i < numPoints; i++)
        {
            on[i] = (flags[i] & 0x01) != 0;
        }

        return new RawGlyph(xs, ys, on, endPts, instructions);
    }

    internal sealed class RawGlyph
    {
        public RawGlyph(int[] x, int[] y, bool[] onCurve, int[] contourEnds, byte[] instructions)
        {
            X = x;
            Y = y;
            OnCurve = onCurve;
            ContourEnds = contourEnds;
            Instructions = instructions;
        }

        public int[] X { get; }

        public int[] Y { get; }

        public bool[] OnCurve { get; }

        public int[] ContourEnds { get; }

        public byte[] Instructions { get; }
    }

    private int GetLeftSideBearing(int glyphId)
        => glyphId >= 0 && glyphId < _leftSideBearings.Length ? _leftSideBearings[glyphId] : 0;

    private int GlyphXMin(int glyphId)
    {
        if (glyphId < 0 || glyphId + 1 >= _loca.Length || _loca[glyphId + 1] <= _loca[glyphId])
        {
            return 0;
        }

        var r = new BigEndianReader(_data) { Position = _glyfOffset + _loca[glyphId] + 2 };
        return r.ReadS16();
    }

    private static IReadOnlyList<GlyphContour> ShiftX(IReadOnlyList<GlyphContour> contours, double dx)
    {
        var shifted = new List<GlyphContour>(contours.Count);
        foreach (GlyphContour c in contours)
        {
            var segs = new List<GlyphSegment>(c.Segments.Count);
            foreach (GlyphSegment s in c.Segments)
            {
                segs.Add(s.Type switch
                {
                    SegmentType.Line => GlyphSegment.Line(s.EndX + dx, s.EndY),
                    SegmentType.Quadratic => GlyphSegment.Quadratic(s.Control1X + dx, s.Control1Y, s.EndX + dx, s.EndY),
                    _ => GlyphSegment.Cubic(s.Control1X + dx, s.Control1Y, s.Control2X + dx, s.Control2Y, s.EndX + dx, s.EndY),
                });
            }

            shifted.Add(new GlyphContour(c.StartX + dx, c.StartY, segs));
        }

        return shifted;
    }

    private void AppendGlyph(int glyphId, ContourBuilder builder, int depth)
    {
        if (depth > MaxCompositeDepth || glyphId < 0 || glyphId + 1 >= _loca.Length)
        {
            return;
        }

        int start = _glyfOffset + _loca[glyphId];
        int end = _glyfOffset + _loca[glyphId + 1];
        if (end <= start)
        {
            return; // empty glyph (e.g. space)
        }

        var r = new BigEndianReader(_data) { Position = start };
        int numberOfContours = r.ReadS16();
        r.Position += 8; // skip xMin/yMin/xMax/yMax

        if (numberOfContours >= 0)
        {
            AppendSimpleGlyph(r, numberOfContours, builder);
        }
        else
        {
            AppendCompositeGlyph(r, builder, depth);
        }
    }

    private static void AppendSimpleGlyph(BigEndianReader r, int numberOfContours, ContourBuilder builder)
    {
        if (numberOfContours == 0)
        {
            return;
        }

        var endPts = new int[numberOfContours];
        for (int i = 0; i < numberOfContours; i++)
        {
            endPts[i] = r.ReadU16();
        }

        int numPoints = endPts[numberOfContours - 1] + 1;
        if (numPoints <= 0)
        {
            return;
        }

        int instructionLength = r.ReadU16();
        r.Position += instructionLength; // skip hinting instructions (F6)

        var flags = new byte[numPoints];
        for (int i = 0; i < numPoints;)
        {
            byte f = r.ReadU8();
            flags[i++] = f;
            if ((f & 0x08) != 0) // REPEAT
            {
                int repeat = r.ReadU8();
                for (int k = 0; k < repeat && i < numPoints; k++)
                {
                    flags[i++] = f;
                }
            }
        }

        var xs = new int[numPoints];
        int x = 0;
        for (int i = 0; i < numPoints; i++)
        {
            byte f = flags[i];
            if ((f & 0x02) != 0) // X_SHORT
            {
                int dx = r.ReadU8();
                x += (f & 0x10) != 0 ? dx : -dx;
            }
            else if ((f & 0x10) == 0) // not same → S16 delta
            {
                x += r.ReadS16();
            }

            xs[i] = x;
        }

        var ys = new int[numPoints];
        int y = 0;
        for (int i = 0; i < numPoints; i++)
        {
            byte f = flags[i];
            if ((f & 0x04) != 0) // Y_SHORT
            {
                int dy = r.ReadU8();
                y += (f & 0x20) != 0 ? dy : -dy;
            }
            else if ((f & 0x20) == 0)
            {
                y += r.ReadS16();
            }

            ys[i] = y;
        }

        int startIndex = 0;
        for (int c = 0; c < numberOfContours; c++)
        {
            int endIndex = endPts[c];
            EmitContour(xs, ys, flags, startIndex, endIndex, builder);
            startIndex = endIndex + 1;
        }
    }

    // Decomposes one contour's TrueType points (with implied on-curve midpoints) into segments.
    private static void EmitContour(int[] xs, int[] ys, byte[] flags, int from, int to, ContourBuilder builder)
    {
        int n = to - from + 1;
        if (n < 1)
        {
            return;
        }

        bool On(int i) => (flags[from + i] & 0x01) != 0;
        double Px(int i) => xs[from + i];
        double Py(int i) => ys[from + i];

        // Choose the contour start by the first/last point (the FreeType convention), so the decomposed
        // outline is canonical: start at point 0 if on-curve, else the last on-curve point, else the
        // midpoint of the first and last (both off-curve).
        double startX;
        double startY;
        int begin;
        int last;
        if (On(0))
        {
            startX = Px(0);
            startY = Py(0);
            begin = 1;
            last = n - 1;
        }
        else if (On(n - 1))
        {
            startX = Px(n - 1);
            startY = Py(n - 1);
            begin = 0;
            last = n - 2;
        }
        else
        {
            startX = (Px(0) + Px(n - 1)) / 2.0;
            startY = (Py(0) + Py(n - 1)) / 2.0;
            begin = 0;
            last = n - 1;
        }

        var seqX = new List<double>(n + 1);
        var seqY = new List<double>(n + 1);
        var seqOn = new List<bool>(n + 1);
        for (int i = begin; i <= last; i++)
        {
            seqX.Add(Px(i));
            seqY.Add(Py(i));
            seqOn.Add(On(i));
        }

        seqX.Add(startX); // close back to the start
        seqY.Add(startY);
        seqOn.Add(true);

        builder.MoveTo(startX, startY);

        int idx = 0;
        while (idx < seqX.Count)
        {
            if (seqOn[idx])
            {
                builder.LineTo(seqX[idx], seqY[idx]);
                idx++;
            }
            else
            {
                double cx = seqX[idx];
                double cy = seqY[idx];
                double ex;
                double ey;
                if (idx + 1 < seqX.Count && seqOn[idx + 1])
                {
                    ex = seqX[idx + 1];
                    ey = seqY[idx + 1];
                    idx += 2;
                }
                else if (idx + 1 < seqX.Count)
                {
                    ex = (cx + seqX[idx + 1]) / 2.0; // implied on-curve midpoint
                    ey = (cy + seqY[idx + 1]) / 2.0;
                    idx += 1;
                }
                else
                {
                    ex = startX;
                    ey = startY;
                    idx += 1;
                }

                builder.QuadTo(cx, cy, ex, ey);
            }
        }
    }

    private void AppendCompositeGlyph(BigEndianReader r, ContourBuilder builder, int depth)
    {
        while (true)
        {
            int flags = r.ReadU16();
            int glyphIndex = r.ReadU16();

            double arg1;
            double arg2;
            if ((flags & 0x0001) != 0) // ARG_1_AND_2_ARE_WORDS
            {
                arg1 = r.ReadS16();
                arg2 = r.ReadS16();
            }
            else
            {
                arg1 = r.ReadS8();
                arg2 = r.ReadS8();
            }

            double a = 1, b = 0, c = 0, d = 1;
            if ((flags & 0x0008) != 0) // WE_HAVE_A_SCALE
            {
                a = d = r.ReadF2Dot14();
            }
            else if ((flags & 0x0040) != 0) // X_AND_Y_SCALE
            {
                a = r.ReadF2Dot14();
                d = r.ReadF2Dot14();
            }
            else if ((flags & 0x0080) != 0) // TWO_BY_TWO
            {
                a = r.ReadF2Dot14();
                b = r.ReadF2Dot14();
                c = r.ReadF2Dot14();
                d = r.ReadF2Dot14();
            }

            // ARGS_ARE_XY_VALUES (0x0002): args are offsets; point-matching form is not supported (treated as 0,0).
            double dx = (flags & 0x0002) != 0 ? arg1 : 0;
            double dy = (flags & 0x0002) != 0 ? arg2 : 0;

            var sub = new ContourBuilder();
            AppendGlyph(glyphIndex, sub, depth + 1);
            foreach (GlyphContour contour in sub.Build())
            {
                AppendTransformed(contour, builder, a, b, c, d, dx, dy);
            }

            if ((flags & 0x0020) == 0) // MORE_COMPONENTS
            {
                break;
            }
        }
    }

    private static void AppendTransformed(GlyphContour contour, ContourBuilder builder, double a, double b, double c, double d, double dx, double dy)
    {
        (double X, double Y) T(double x, double y) => ((a * x) + (c * y) + dx, (b * x) + (d * y) + dy);

        (double sx, double sy) = T(contour.StartX, contour.StartY);
        builder.MoveTo(sx, sy);
        foreach (GlyphSegment seg in contour.Segments)
        {
            (double ex, double ey) = T(seg.EndX, seg.EndY);
            switch (seg.Type)
            {
                case SegmentType.Line:
                    builder.LineTo(ex, ey);
                    break;
                case SegmentType.Quadratic:
                {
                    (double cx, double cy) = T(seg.Control1X, seg.Control1Y);
                    builder.QuadTo(cx, cy, ex, ey);
                    break;
                }

                default:
                {
                    (double c1x, double c1y) = T(seg.Control1X, seg.Control1Y);
                    (double c2x, double c2y) = T(seg.Control2X, seg.Control2Y);
                    builder.CubicTo(c1x, c1y, c2x, c2y, ex, ey);
                    break;
                }
            }
        }
    }

    private static int[] ReadLoca(BigEndianReader r, int locaOffset, int numGlyphs, int indexToLocFormat)
    {
        var loca = new int[numGlyphs + 1];
        r.Position = locaOffset;
        if (indexToLocFormat == 0)
        {
            for (int i = 0; i <= numGlyphs; i++)
            {
                loca[i] = r.ReadU16() * 2;
            }
        }
        else
        {
            for (int i = 0; i <= numGlyphs; i++)
            {
                loca[i] = (int)r.ReadU32();
            }
        }

        return loca;
    }

    private static (int[] Advances, int[] LeftSideBearings) ReadHmtx(BigEndianReader r, Dictionary<string, (int Offset, int Length)> tables, int numberOfHMetrics, int numGlyphs)
    {
        var advances = new int[numGlyphs];
        var lsbs = new int[numGlyphs];
        if (numberOfHMetrics <= 0 || !tables.TryGetValue("hmtx", out (int Offset, int Length) hmtx))
        {
            return (advances, lsbs);
        }

        r.Position = hmtx.Offset;
        int last = 0;
        for (int i = 0; i < numGlyphs; i++)
        {
            if (i < numberOfHMetrics)
            {
                last = r.ReadU16();
                lsbs[i] = r.ReadS16();
            }
            else
            {
                lsbs[i] = r.ReadS16(); // trailing leftSideBearing array
            }

            advances[i] = last;
        }

        return (advances, lsbs);
    }

    private static string ReadTag(BigEndianReader r)
    {
        char a = (char)r.ReadU8();
        char b = (char)r.ReadU8();
        char c = (char)r.ReadU8();
        char d = (char)r.ReadU8();
        return new string(new[] { a, b, c, d });
    }
}
