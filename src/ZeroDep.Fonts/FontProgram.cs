using System;

namespace ZeroDep.Fonts;

/// <summary>The kind of font program a <see cref="FontProgram"/> wraps.</summary>
public enum FontProgramKind
{
    /// <summary>A glyf-based TrueType program (PDF <c>FontFile2</c>).</summary>
    TrueType,

    /// <summary>A CFF / Type1C program, bare or OpenType-wrapped (PDF <c>FontFile3</c>).</summary>
    Cff,

    /// <summary>A Type 1 program (PDF <c>FontFile</c>).</summary>
    Type1,
}

/// <summary>
/// Unified entry point over the embedded font-program parsers. Sniffs the program kind and exposes a single
/// glyph-access surface (by glyph id, by name, by code point, by CID) plus optional TrueType hinting, so
/// callers do not need to know which underlying format a PDF embedded (ADR-0005 §F7).
/// </summary>
public sealed class FontProgram
{
    private readonly TrueTypeFont? _ttf;
    private readonly CffFont? _cff;
    private readonly Type1Font? _t1;

    private FontProgram(TrueTypeFont? ttf, CffFont? cff, Type1Font? t1, FontProgramKind kind)
    {
        _ttf = ttf;
        _cff = cff;
        _t1 = t1;
        Kind = kind;
    }

    /// <summary>The detected program kind.</summary>
    public FontProgramKind Kind { get; }

    /// <summary>The em-square size in font units.</summary>
    public int UnitsPerEm => _ttf?.UnitsPerEm ?? _cff?.UnitsPerEm ?? _t1?.UnitsPerEm ?? 1000;

    /// <summary>The number of glyphs (for name-keyed Type 1, the number of named glyphs).</summary>
    public int GlyphCount => _ttf?.GlyphCount ?? _cff?.GlyphCount ?? _t1?.GlyphCount ?? 0;

    /// <summary>True if this is a CID-keyed CFF program.</summary>
    public bool IsCidKeyed => _cff?.IsCidKeyed ?? false;

    /// <summary>True if grid-fitting (hinting) is available (TrueType with <c>fpgm</c>/<c>prep</c>).</summary>
    public bool SupportsHinting => _ttf?.HasHinting ?? false;

    /// <summary>True for a name-keyed program (Type 1), where <see cref="GetGlyphByName"/> is the primary access.</summary>
    public bool IsNameKeyed => _t1 is not null;

    /// <summary>Sniffs the program kind from the leading bytes and parses it.</summary>
    /// <param name="data">The decoded font-program bytes.</param>
    public static FontProgram Load(byte[] data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        FontProgramKind kind = Detect(data);
        switch (kind)
        {
            case FontProgramKind.Type1:
                return new FontProgram(null, null, Type1Font.Load(data), kind);
            case FontProgramKind.Cff:
                return new FontProgram(null, CffFont.Load(data), null, kind);
            default:
                return new FontProgram(TrueTypeFont.Load(data), null, null, kind);
        }
    }

    /// <summary>Returns the outline (font units) for a glyph id, or <see cref="GlyphOutline.Empty"/>.</summary>
    /// <param name="glyphId">The glyph id.</param>
    public GlyphOutline GetGlyph(int glyphId)
    {
        if (_ttf is not null)
        {
            return _ttf.GetGlyph(glyphId);
        }

        if (_cff is not null)
        {
            return _cff.GetGlyph(glyphId);
        }

        if (_t1 is not null && glyphId >= 0 && glyphId < _t1.GlyphNames.Count)
        {
            return _t1.GetGlyph(_t1.GlyphNames[glyphId]);
        }

        return GlyphOutline.Empty;
    }

    /// <summary>Returns the outline for a named glyph (Type 1), or <see cref="GlyphOutline.Empty"/>.</summary>
    /// <param name="name">The glyph name.</param>
    public GlyphOutline GetGlyphByName(string name)
        => _t1 is not null ? _t1.GetGlyph(name) : GlyphOutline.Empty;

    /// <summary>
    /// Returns the hinted outline at a given ppem (coordinates in 26.6 device pixels) for TrueType; for other
    /// programs, returns the linearly scaled (unhinted) outline at that ppem.
    /// </summary>
    /// <param name="glyphId">The glyph id.</param>
    /// <param name="pixelsPerEm">The target em size in pixels.</param>
    public GlyphOutline GetHintedGlyph(int glyphId, int pixelsPerEm)
    {
        if (_ttf is not null)
        {
            return _ttf.GetHintedGlyph(glyphId, pixelsPerEm);
        }

        // No native hinting: scale the outline to 26.6 device pixels for a uniform contract.
        GlyphOutline outline = GetGlyph(glyphId);
        return ScaleToDevice(outline, pixelsPerEm);
    }

    /// <summary>Maps a Unicode code point to a glyph id via the program's cmap (TrueType), or 0.</summary>
    /// <param name="codepoint">The Unicode code point.</param>
    public int MapCodepointToGlyph(int codepoint) => _ttf?.MapCodepointToGlyph(codepoint) ?? 0;

    /// <summary>Maps a CID to a glyph id (CID-keyed CFF); otherwise returns the CID unchanged when in range.</summary>
    /// <param name="cid">The character identifier.</param>
    public int GlyphIdForCid(int cid)
    {
        if (_cff is not null)
        {
            return _cff.GlyphIdForCid(cid);
        }

        return cid >= 0 && cid < GlyphCount ? cid : 0;
    }

    private GlyphOutline ScaleToDevice(GlyphOutline outline, int ppem)
    {
        if (outline.IsEmpty || ppem <= 0)
        {
            return outline;
        }

        double s = (double)ppem * 64 / UnitsPerEm;
        var contours = new System.Collections.Generic.List<GlyphContour>(outline.Contours.Count);
        foreach (GlyphContour c in outline.Contours)
        {
            var segs = new System.Collections.Generic.List<GlyphSegment>(c.Segments.Count);
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

        return new GlyphOutline(contours, (int)Math.Round((double)outline.AdvanceWidth * ppem * 64 / UnitsPerEm));
    }

    private static FontProgramKind Detect(byte[] data)
    {
        if (data.Length >= 4)
        {
            uint v = (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);
            if (v == 0x4F54544F) // 'OTTO'
            {
                return FontProgramKind.Cff;
            }

            if (v == 0x00010000 || v == 0x74727565 /* 'true' */ || v == 0x74746366 /* 'ttcf' */)
            {
                return FontProgramKind.TrueType;
            }
        }

        // Type 1: clear-text PostScript header or a bare CFF (first byte = major version 1).
        if (LooksLikeType1(data))
        {
            return FontProgramKind.Type1;
        }

        if (data.Length >= 4 && data[0] == 0x01)
        {
            return FontProgramKind.Cff; // bare CFF
        }

        return FontProgramKind.TrueType;
    }

    private static bool LooksLikeType1(byte[] data)
    {
        if (data.Length >= 2 && data[0] == (byte)'%' && data[1] == (byte)'!')
        {
            return true;
        }

        if (data.Length >= 2 && data[0] == 0x80 && data[1] == 0x01)
        {
            return true; // PFB segment marker
        }

        int limit = Math.Min(data.Length - 5, 1024);
        for (int i = 0; i < limit; i++)
        {
            if (data[i] == 'e' && data[i + 1] == 'e' && data[i + 2] == 'x' && data[i + 3] == 'e' && data[i + 4] == 'c')
            {
                return true;
            }
        }

        return false;
    }
}
