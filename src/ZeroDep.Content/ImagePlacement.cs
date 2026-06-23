using ZeroDep.Objects;

namespace ZeroDep.Content;

/// <summary>An image drawn on a page: the image XObject plus the CTM in effect at the draw.</summary>
internal sealed class ImagePlacement
{
    public ImagePlacement(string name, PdfStream image, Matrix transform)
    {
        Name = name;
        Image = image;
        Transform = transform;
    }

    /// <summary>The resource name the image was invoked by (e.g. <c>Im0</c>).</summary>
    public string Name { get; }

    /// <summary>The image XObject stream.</summary>
    public PdfStream Image { get; }

    /// <summary>The current transformation matrix at the point the image was drawn.</summary>
    public Matrix Transform { get; }
}
