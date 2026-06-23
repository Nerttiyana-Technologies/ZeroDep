namespace ZeroDep.Abstractions;

/// <summary>
/// Content-free, structural classification used for aggregate statistics.
/// Categories are derived purely from how a document is built — never from its content.
/// </summary>
public enum DocumentCategory
{
    /// <summary>Not yet classified.</summary>
    Unknown = 0,

    /// <summary>Failed integrity validation.</summary>
    Rejected,

    /// <summary>Encrypted, and authentication failed.</summary>
    EncryptedUnreadable,

    /// <summary>Page area dominated by raster images with little or no real text.</summary>
    ScannedImageOnly,

    /// <summary>Image-dominated and carrying an invisible OCR text layer.</summary>
    ScannedWithOcr,

    /// <summary>Real extractable text with negligible raster image area.</summary>
    DigitalText,

    /// <summary>Contains an interactive AcroForm with fields.</summary>
    FormBased,

    /// <summary>Significant text and significant image content.</summary>
    Mixed,
}
