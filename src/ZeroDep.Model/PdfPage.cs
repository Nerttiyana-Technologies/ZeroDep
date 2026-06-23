using ZeroDep.Objects;

namespace ZeroDep.Model;

/// <summary>A leaf page with its inherited attributes resolved.</summary>
internal sealed class PdfPage
{
    public PdfPage(int index, PdfDictionary dictionary, PdfRectangle mediaBox, int rotation, PdfDictionary? resources)
    {
        Index = index;
        Dictionary = dictionary;
        MediaBox = mediaBox;
        Rotation = rotation;
        Resources = resources;
    }

    /// <summary>Zero-based page index in document order.</summary>
    public int Index { get; }

    /// <summary>The page dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>The effective MediaBox (inherited if not declared on the page).</summary>
    public PdfRectangle MediaBox { get; }

    /// <summary>The page rotation, normalized to 0/90/180/270.</summary>
    public int Rotation { get; }

    /// <summary>The effective resource dictionary (inherited if not declared on the page).</summary>
    public PdfDictionary? Resources { get; }
}
