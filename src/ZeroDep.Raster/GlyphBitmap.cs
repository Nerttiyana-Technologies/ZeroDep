using System;

namespace ZeroDep.Raster;

/// <summary>
/// A rasterized glyph: an 8-bit anti-aliased <b>coverage</b> mask (0 = empty, 255 = fully covered) plus the
/// pen-relative placement. Colour fill and compositing onto a page are the renderer's job (2.1); this is the
/// mask only (ADR-0005 §2.3).
/// </summary>
public sealed class GlyphBitmap
{
    /// <summary>Creates a glyph bitmap.</summary>
    /// <param name="width">Bitmap width in pixels.</param>
    /// <param name="height">Bitmap height in pixels.</param>
    /// <param name="left">X offset of the bitmap's left edge from the pen origin (pixels; may be negative).</param>
    /// <param name="top">Y offset of the bitmap's top edge above the baseline (pixels; up positive).</param>
    /// <param name="coverage">Row-major 8-bit coverage, length <c>Width*Height</c>.</param>
    public GlyphBitmap(int width, int height, int left, int top, byte[] coverage)
    {
        Width = width;
        Height = height;
        Left = left;
        Top = top;
        Coverage = coverage ?? throw new ArgumentNullException(nameof(coverage));
    }

    /// <summary>Bitmap width in pixels.</summary>
    public int Width { get; }

    /// <summary>Bitmap height in pixels.</summary>
    public int Height { get; }

    /// <summary>X offset of the bitmap's left edge from the pen origin (pixels).</summary>
    public int Left { get; }

    /// <summary>Y offset of the bitmap's top edge above the baseline (pixels, up positive).</summary>
    public int Top { get; }

    /// <summary>Row-major 8-bit coverage mask (0–255), length <c>Width*Height</c>.</summary>
    public byte[] Coverage { get; }

    /// <summary>An empty (zero-size) bitmap.</summary>
    public static GlyphBitmap Empty { get; } = new GlyphBitmap(0, 0, 0, 0, Array.Empty<byte>());
}
