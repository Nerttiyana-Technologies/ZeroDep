using System;
using ZeroDep.Fonts;

namespace ZeroDep.Raster;

/// <summary>
/// Convenience renderer tying a <see cref="FontProgram"/> to the <see cref="GlyphRasterizer"/>: resolves the
/// outline (optionally grid-fitted) and rasterizes it to a coverage <see cref="GlyphBitmap"/> at a target
/// pixel size (ADR-0005 §F7).
/// </summary>
public static class GlyphRenderer
{
    /// <summary>Renders a glyph to an anti-aliased coverage bitmap at the given pixel size.</summary>
    /// <param name="font">The font program.</param>
    /// <param name="glyphId">The glyph id.</param>
    /// <param name="pixelSize">The target em size in pixels.</param>
    /// <param name="hinted">If true and supported, grid-fit the outline before rasterizing.</param>
    public static GlyphBitmap Render(FontProgram font, int glyphId, double pixelSize, bool hinted = false)
    {
        if (font is null)
        {
            throw new ArgumentNullException(nameof(font));
        }

        if (pixelSize <= 0)
        {
            return GlyphBitmap.Empty;
        }

        if (hinted)
        {
            int ppem = (int)Math.Round(pixelSize);
            if (ppem <= 0)
            {
                ppem = 1;
            }

            // Hinted/scaled outlines are already in 26.6 device pixels; render at 1/64 scale.
            GlyphOutline hintedOutline = font.GetHintedGlyph(glyphId, ppem);
            return GlyphRasterizer.Render(hintedOutline, 1.0 / 64.0);
        }

        GlyphOutline outline = font.GetGlyph(glyphId);
        double scale = pixelSize / font.UnitsPerEm;
        return GlyphRasterizer.Render(outline, scale);
    }
}
