namespace ZeroDep.Abstractions;

/// <summary>The provenance of an extracted <see cref="TextRunInfo"/>.</summary>
public enum TextSource
{
    /// <summary>Text that was present in the document (a content-stream run or an embedded OCR layer).</summary>
    Embedded = 0,

    /// <summary>
    /// Text recovered by OCR from a raster image that had no embedded text. It is a recognition
    /// result, not ground truth — consumers should trust it according to its confidence.
    /// </summary>
    OcrGenerated,
}
