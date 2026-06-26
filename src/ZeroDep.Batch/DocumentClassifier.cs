using System;
using System.Collections.Generic;
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
            // OCR-recovered text marks the page as ScannedWithOcr, but is NOT counted as real
            // (embedded, visible) text — the structural categories stay content-honest.
            if (run.Source == TextSource.OcrGenerated)
            {
                ocrPresent = true;
            }
            else if (run.IsOcrLayer)
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

    /// <summary>
    /// Rolls the per-page classes (ADR-0003) up into a document-level <see cref="DocumentCategory"/>: a
    /// form page anywhere makes the document form-based; otherwise an all-same set maps to that category
    /// and any genuine mix maps to <see cref="DocumentCategory.Mixed"/>. Consistent with
    /// <see cref="Classify(DocumentAnalysis)"/> on single-class documents (ADR-0003 Z-G2).
    /// </summary>
    /// <param name="pages">The per-page classifications.</param>
    public static DocumentCategory ClassifyPages(IReadOnlyList<PageClassification> pages)
    {
        if (pages is null)
        {
            throw new ArgumentNullException(nameof(pages));
        }

        DocumentCategory? single = null;
        bool mixed = false;
        foreach (PageClassification page in pages)
        {
            if (page.Class == PageContentClass.FormPage)
            {
                return DocumentCategory.FormBased;
            }

            DocumentCategory? category = MapPage(page.Class);
            if (category is not DocumentCategory c)
            {
                continue; // Empty pages do not constrain the roll-up
            }

            if (single is null)
            {
                single = c;
            }
            else if (single != c)
            {
                mixed = true;
            }
        }

        if (mixed)
        {
            return DocumentCategory.Mixed;
        }

        return single ?? DocumentCategory.DigitalText;
    }

    private static DocumentCategory? MapPage(PageContentClass cls)
        => cls switch
        {
            PageContentClass.DigitalText => DocumentCategory.DigitalText,
            PageContentClass.TableOrComplexLayout => DocumentCategory.DigitalText,
            PageContentClass.ScannedImageOnly => DocumentCategory.ScannedImageOnly,
            PageContentClass.ScannedWithOcr => DocumentCategory.ScannedWithOcr,
            PageContentClass.Mixed => DocumentCategory.Mixed,
            PageContentClass.FormPage => DocumentCategory.FormBased,
            _ => null, // Empty
        };
}
