using System;
using Xunit;
using ZeroDep.Abstractions;

namespace ZeroDep.Batch.Tests;

public sealed class DocumentClassifierTests
{
    private static readonly ClassificationThresholds Thresholds = ClassificationThresholds.Default;

    [Fact]
    public void RejectedDocumentIsRejected()
    {
        var analysis = new DocumentAnalysis { Status = DocumentStatus.Rejected };
        Assert.Equal(DocumentCategory.Rejected, DocumentClassifier.Classify(analysis, Thresholds));
    }

    [Fact]
    public void EncryptedAuthFailureIsEncryptedUnreadable()
    {
        var analysis = new DocumentAnalysis
        {
            Security = new SecurityInfo { IsEncrypted = true, Authentication = AuthenticationResult.Failed },
        };
        Assert.Equal(DocumentCategory.EncryptedUnreadable, DocumentClassifier.Classify(analysis, Thresholds));
    }

    [Fact]
    public void FormWithFieldsIsFormBased()
    {
        var analysis = new DocumentAnalysis
        {
            Form = new AcroFormReport
            {
                HasAcroForm = true,
                Fields = new[] { new FormFieldInfo { FullyQualifiedName = "a", FieldType = "Tx" } },
            },
        };
        Assert.Equal(DocumentCategory.FormBased, DocumentClassifier.Classify(analysis, Thresholds));
    }

    [Fact]
    public void ImageDominatedWithOcrLayerIsScannedWithOcr()
    {
        var analysis = new DocumentAnalysis
        {
            ImageAreaFraction = 0.95,
            Images = new[] { new ImageDpiInfo { PageIndex = 0, EffectiveDpi = 200 } },
            TextRuns = new[] { new TextRunInfo { Text = "scanned words", RenderMode = 3, IsOcrLayer = true } },
        };
        Assert.Equal(DocumentCategory.ScannedWithOcr, DocumentClassifier.Classify(analysis, Thresholds));
    }

    [Fact]
    public void ImageDominatedWithoutTextIsScannedImageOnly()
    {
        var analysis = new DocumentAnalysis
        {
            ImageAreaFraction = 0.9,
            Images = new[] { new ImageDpiInfo { PageIndex = 0, EffectiveDpi = 120 } },
        };
        Assert.Equal(DocumentCategory.ScannedImageOnly, DocumentClassifier.Classify(analysis, Thresholds));
    }

    [Fact]
    public void ImageDominatedWithRealTextIsMixed()
    {
        var analysis = new DocumentAnalysis
        {
            ImageAreaFraction = 0.85,
            Images = new[] { new ImageDpiInfo { PageIndex = 0, EffectiveDpi = 300 } },
            TextRuns = new[] { new TextRunInfo { Text = "a paragraph of real visible body text here" } },
        };
        Assert.Equal(DocumentCategory.Mixed, DocumentClassifier.Classify(analysis, Thresholds));
    }

    [Fact]
    public void ImageDominatedWithOcrGeneratedText_IsScannedWithOcr()
    {
        var analysis = new DocumentAnalysis
        {
            ImageAreaFraction = 0.95,
            Images = new[] { new ImageDpiInfo { PageIndex = 0, EffectiveDpi = 150 } },
            TextRuns = new[] { new TextRunInfo { Text = "ocr-recovered words", Source = TextSource.OcrGenerated, Confidence = 0.9 } },
        };
        Assert.Equal(DocumentCategory.ScannedWithOcr, DocumentClassifier.Classify(analysis, Thresholds));
    }

    [Fact]
    public void TextWithNegligibleImagesIsDigitalText()
    {
        var analysis = new DocumentAnalysis
        {
            ImageAreaFraction = 0.02,
            TextRuns = new[] { new TextRunInfo { Text = "a full page of ordinary digital text content" } },
        };
        Assert.Equal(DocumentCategory.DigitalText, DocumentClassifier.Classify(analysis, Thresholds));
    }

    [Fact]
    public void NullArgumentsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => DocumentClassifier.Classify(null!, Thresholds));
        Assert.Throws<ArgumentNullException>(() => DocumentClassifier.Classify(new DocumentAnalysis(), null!));
    }
}
