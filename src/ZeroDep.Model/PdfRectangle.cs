using System;

namespace ZeroDep.Model;

/// <summary>A PDF rectangle (e.g. a page's MediaBox), in user-space points.</summary>
internal readonly struct PdfRectangle
{
    public PdfRectangle(double lowerLeftX, double lowerLeftY, double upperRightX, double upperRightY)
    {
        LowerLeftX = lowerLeftX;
        LowerLeftY = lowerLeftY;
        UpperRightX = upperRightX;
        UpperRightY = upperRightY;
    }

    public double LowerLeftX { get; }

    public double LowerLeftY { get; }

    public double UpperRightX { get; }

    public double UpperRightY { get; }

    /// <summary>Absolute width in points.</summary>
    public double Width => Math.Abs(UpperRightX - LowerLeftX);

    /// <summary>Absolute height in points.</summary>
    public double Height => Math.Abs(UpperRightY - LowerLeftY);

    /// <summary>US Letter (612 x 792 pt), used when no MediaBox is declared.</summary>
    public static PdfRectangle Default => new PdfRectangle(0, 0, 612, 792);
}
