using System;
using System.Collections.Generic;
using ZeroDep.Abstractions;

namespace ZeroDep.Ocr;

/// <summary>
/// Runs OCR over a document's embedded images and augments its <see cref="DocumentAnalysis"/> with
/// the recovered text (ADR-0002). Recovered runs are tagged <see cref="TextSource.OcrGenerated"/>
/// with confidence and are never merged silently with embedded text. Intended for documents whose
/// pages are scanned images with no embedded text layer (classified <c>ScannedImageOnly</c>).
/// </summary>
public static class OcrProcessor
{
    /// <summary>
    /// Recognizes text in the supplied images and returns the analysis with OCR-generated text runs
    /// appended. The original analysis is returned unchanged when nothing is recovered.
    /// </summary>
    /// <param name="analysis">The structural analysis to augment.</param>
    /// <param name="images">The document's embedded images (from <c>PdfAnalyzer.ExtractImages</c>).</param>
    /// <param name="engine">The OCR engine adapter.</param>
    /// <param name="options">Recognition options (languages, confidence threshold).</param>
    public static DocumentAnalysis Augment(DocumentAnalysis analysis, IReadOnlyList<PdfImageInfo> images, IOcrEngine engine, OcrOptions options)
    {
        if (analysis is null)
        {
            throw new ArgumentNullException(nameof(analysis));
        }

        if (images is null)
        {
            throw new ArgumentNullException(nameof(images));
        }

        if (engine is null)
        {
            throw new ArgumentNullException(nameof(engine));
        }

        options ??= new OcrOptions();

        var ocrRuns = new List<TextRunInfo>();
        foreach (PdfImageInfo image in images)
        {
            DecodedImage? decoded;
            try
            {
                decoded = Decode(image);
            }
            catch (Exception)
            {
                continue;   // undecodable image is isolated, never aborts the document
            }

            if (decoded is null)
            {
                continue;   // filter not supported (yet)
            }

            OcrResult result;
            try
            {
                result = engine.Recognize(decoded, options);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (OcrLine line in result.Lines)
            {
                if (line.Confidence < options.MinConfidence)
                {
                    continue;
                }

                ocrRuns.Add(new TextRunInfo
                {
                    PageIndex = image.PageIndex,
                    Text = line.Text,
                    X = line.Bounds.X,
                    Y = line.Bounds.Y,
                    Width = line.Bounds.Width,
                    FontSize = line.Bounds.Height,
                    Source = TextSource.OcrGenerated,
                    Confidence = line.Confidence,
                });
            }
        }

        if (ocrRuns.Count == 0)
        {
            return analysis;
        }

        var merged = new List<TextRunInfo>(analysis.TextRuns);
        merged.AddRange(ocrRuns);

        return new DocumentAnalysis
        {
            SchemaVersion = analysis.SchemaVersion,
            Status = analysis.Status,
            Rejection = analysis.Rejection,
            PageCount = analysis.PageCount,
            Security = analysis.Security,
            Images = analysis.Images,
            TextRuns = merged,
            Form = analysis.Form,
            Coverage = analysis.Coverage,
            ImageAreaFraction = analysis.ImageAreaFraction,
        };
    }

    // Decodes a supported single-filter image to pixels, or returns null if the filter is unsupported.
    private static DecodedImage? Decode(PdfImageInfo image)
    {
        switch (image.Filter)
        {
            case "DCTDecode":
                return OcrImageConverter.FromJpeg(image.EncodedData);

            case "CCITTFaxDecode" when image.Ccitt is not null:
                return OcrImageConverter.FromCcitt(image.EncodedData, image.Ccitt);

            case "JBIG2Decode":
                return OcrImageConverter.FromJbig2(image.EncodedData, image.Jbig2Globals, image.DeclaredWidth, image.DeclaredHeight);

            case "JPXDecode":
                return OcrImageConverter.FromJpx(image.EncodedData, image.DeclaredWidth, image.DeclaredHeight);

            default:
                return null;   // other filters not yet supported
        }
    }
}
