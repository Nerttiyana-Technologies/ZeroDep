namespace ZeroDep.Abstractions;

/// <summary>Effective-resolution metrics for a single placed image (Feature A).</summary>
public sealed class ImageDpiInfo
{
    /// <summary>Zero-based index of the page the image appears on.</summary>
    public int PageIndex { get; init; }

    /// <summary>The resource name the image was invoked by (e.g. <c>Im0</c>).</summary>
    public string? ResourceName { get; init; }

    /// <summary>Declared pixel width (<c>/Width</c>).</summary>
    public int PixelWidth { get; init; }

    /// <summary>Declared pixel height (<c>/Height</c>).</summary>
    public int PixelHeight { get; init; }

    /// <summary>Rendered width on the page, in points.</summary>
    public double RenderedWidthPoints { get; init; }

    /// <summary>Rendered height on the page, in points.</summary>
    public double RenderedHeightPoints { get; init; }

    /// <summary>Horizontal resolution in dots per inch.</summary>
    public double HorizontalDpi { get; init; }

    /// <summary>Vertical resolution in dots per inch.</summary>
    public double VerticalDpi { get; init; }

    /// <summary>The effective (lower) resolution used for threshold comparison.</summary>
    public double EffectiveDpi { get; init; }

    /// <summary>The DPI threshold the image was compared against.</summary>
    public int Threshold { get; init; }

    /// <summary>Whether <see cref="EffectiveDpi"/> is below <see cref="Threshold"/>.</summary>
    public bool IsBelowThreshold { get; init; }

    /// <summary>The image's <c>/Filter</c> (filters joined with <c>+</c>), or null if none.</summary>
    public string? Filter { get; init; }

    /// <summary>Whether the image declares a soft mask (<c>/SMask</c>).</summary>
    public bool HasSoftMask { get; init; }

    /// <summary>Whether the image declares an explicit or color-key mask (<c>/Mask</c>).</summary>
    public bool HasMask { get; init; }

    /// <summary>Whether the image is a 1-bit stencil mask (<c>/ImageMask true</c>).</summary>
    public bool IsImageMask { get; init; }

    /// <summary>
    /// The fraction (0–1) of the page's area this placement covers. Small assets (logos, stamps,
    /// signatures) cover only a small fraction and should be ignored when judging a page's scan
    /// quality, to avoid false low-DPI flags on a low-resolution logo over a high-resolution page.
    /// </summary>
    public double PageAreaFraction { get; init; }
}
