using System;
using ZeroDep.Abstractions;

namespace ZeroDep.Batch;

/// <summary>
/// Classifies an analyzed document into a content-free, structural category (ADR Feature H).
/// Every category derives only from how the document is built — never from what it says. No file
/// name, extracted text, field name, or field value influences the result.
/// </summary>
public static class DocumentClassifier
{
    /// <summary>Classifies an analyzed document using the supplied thresholds.</summary>
    /// <param name="analysis">The completed structural analysis.</param>
    /// <param name="thresholds">The reproducible classification thresholds.</param>
    /// <returns>The structural category.</returns>
    public static DocumentCategory Classify(DocumentAnalysis analysis, ClassificationThresholds thresholds)
    {
        if (analysis is null)
        {
            throw new ArgumentNullException(nameof(analysis));
        }

        if (thresholds is null)
        {
            throw new ArgumentNullException(nameof(thresholds));
        }

        if (analysis.Status == DocumentStatus.Rejected)
        {
            return DocumentCategory.Rejected;
        }

        if (analysis.Security.IsEncrypted && analysis.Security.Authentication == AuthenticationResult.Failed)
        {
            return DocumentCategory.EncryptedUnreadable;
        }

        if (analysis.Form.HasAcroForm && analysis.Form.Fields.Count > 0)
        {
            return DocumentCategory.FormBased;
        }

        int realTextChars = 0;
        bool ocrPresent = false;
        foreach (TextRunInfo run in analysis.TextRuns)
        {
            if (run.IsOcrLayer)
            {
                ocrPresent = true;
            }
            else if (run.Text != null)
            {
                realTextChars += run.Text.Length;
            }
        }

        bool hasText = realTextChars >= thresholds.MinTextChars;
        bool imageDominated = analysis.ImageAreaFraction >= thresholds.ImageAreaPercent / 100.0;
        bool hasImages = analysis.Images.Count > 0;

        if (imageDominated)
        {
            if (ocrPresent)
            {
                return DocumentCategory.ScannedWithOcr;
            }

            return hasText ? DocumentCategory.Mixed : DocumentCategory.ScannedImageOnly;
        }

        if (hasText)
        {
            return DocumentCategory.DigitalText;
        }

        return hasImages ? DocumentCategory.ScannedImageOnly : DocumentCategory.DigitalText;
    }

    /// <summary>Classifies an analyzed document using the default thresholds.</summary>
    /// <param name="analysis">The completed structural analysis.</param>
    /// <returns>The structural category.</returns>
    public static DocumentCategory Classify(DocumentAnalysis analysis)
        => Classify(analysis, ClassificationThresholds.Default);
}
