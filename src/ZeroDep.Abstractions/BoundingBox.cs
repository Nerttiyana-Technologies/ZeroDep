namespace ZeroDep.Abstractions;

/// <summary>An axis-aligned bounding box in PDF device space (origin lower-left, Y up).</summary>
public readonly struct BoundingBox
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="width"></param>
    /// <param name="height"></param>
    public BoundingBox(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>Left edge.</summary>
    public double X { get; }

    /// <summary>Bottom edge.</summary>
    public double Y { get; }

    /// <summary>Width.</summary>
    public double Width { get; }

    /// <summary>Height.</summary>
    public double Height { get; }
}
