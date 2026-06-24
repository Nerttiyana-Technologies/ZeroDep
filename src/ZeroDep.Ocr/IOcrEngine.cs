namespace ZeroDep.Ocr;

/// <summary>
/// A pluggable optical-character-recognition engine. Implementations live in opt-in adapter packages
/// (for example <c>ZeroDep.Ocr.Tesseract</c>) so the ZeroDep core stays 100% dependency-free — only a
/// consumer that installs an adapter pulls in an engine and its assets.
/// </summary>
public interface IOcrEngine
{
    /// <summary>Recognizes text in a decoded raster image.</summary>
    /// <param name="image">The decoded image samples to recognize.</param>
    /// <param name="options">Recognition options (languages, confidence threshold).</param>
    /// <returns>The recognized lines with positions and confidence.</returns>
    OcrResult Recognize(DecodedImage image, OcrOptions options);
}
