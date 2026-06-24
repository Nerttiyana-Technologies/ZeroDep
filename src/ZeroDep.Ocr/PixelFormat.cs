namespace ZeroDep.Ocr;

/// <summary>The sample layout of a <see cref="DecodedImage"/>.</summary>
public enum PixelFormat
{
    /// <summary>8-bit grayscale, one byte per pixel.</summary>
    Gray8 = 0,

    /// <summary>24-bit RGB, three bytes per pixel (R, G, B).</summary>
    Rgb24,

    /// <summary>32-bit RGBA, four bytes per pixel (R, G, B, A).</summary>
    Rgba32,
}
