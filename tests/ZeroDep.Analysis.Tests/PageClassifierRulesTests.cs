using Xunit;
using ZeroDep.Abstractions;
using ZeroDep.Analysis;

namespace ZeroDep.Analysis.Tests;

/// <summary>
/// P2 — the gather-then-classify rules (ADR-0003 §2.2), tested via <see cref="PageClassifier.Decide"/>
/// on fabricated signals. Covers the dangerous cases: scanned never → text/form, form-with-table → Mixed,
/// faint scan → not Empty, and ties biasing to the safer class.
/// </summary>
public sealed class PageClassifierRulesTests
{
    private static PageSignals S(
        int textRuns = 0, double coverage = 0, bool widgets = false, int widgetCount = 0,
        bool imageOnly = false, bool ocr = false, int ruling = 0, double columns = 0)
        => new PageSignals
        {
            TextRunCount = textRuns,
            TextCoverageFraction = coverage,
            HasAcroFormWidgets = widgets,
            WidgetCount = widgetCount,
            IsImageOnly = imageOnly,
            OcrLayerPresent = ocr,
            RulingLineCount = ruling,
            ColumnAlignmentScore = columns,
        };

    [Fact]
    public void ScannedImageOnly_WhenDominantImageNoText()
    {
        (PageContentClass cls, _) = PageClassifier.Decide(S(imageOnly: true), imageCount: 1);
        Assert.Equal(PageContentClass.ScannedImageOnly, cls);
    }

    [Fact]
    public void ScannedWithOcr_WhenDominantImageWithOcrLayer()
    {
        (PageContentClass cls, _) = PageClassifier.Decide(S(imageOnly: true, ocr: true), imageCount: 1);
        Assert.Equal(PageContentClass.ScannedWithOcr, cls);
    }

    [Fact]
    public void Mixed_WhenDominantImageWithSubstantialText()
    {
        (PageContentClass cls, _) = PageClassifier.Decide(S(textRuns: 50, coverage: 0.2, imageOnly: true), imageCount: 1);
        Assert.Equal(PageContentClass.Mixed, cls);
    }

    [Fact]
    public void FormPage_WhenWidgetsAndNoTable()
    {
        (PageContentClass cls, _) = PageClassifier.Decide(S(textRuns: 10, coverage: 0.05, widgets: true, widgetCount: 8), imageCount: 0);
        Assert.Equal(PageContentClass.FormPage, cls);
    }

    [Fact]
    public void Mixed_WhenFormPageHasNonWidgetTable()
    {
        // form widgets + a strong non-widget table signal → escalate, never FormPage (Z-G1 dangerous error)
        (PageContentClass cls, _) = PageClassifier.Decide(S(textRuns: 30, coverage: 0.1, widgets: true, widgetCount: 4, ruling: 40), imageCount: 0);
        Assert.Equal(PageContentClass.Mixed, cls);
    }

    [Fact]
    public void DigitalText_ForSimpleTextFlow()
    {
        (PageContentClass cls, double conf) = PageClassifier.Decide(S(textRuns: 200, coverage: 0.3), imageCount: 0);
        Assert.Equal(PageContentClass.DigitalText, cls);
        Assert.True(conf >= 0.9);
    }

    [Fact]
    public void TableOrComplex_WhenTextWithManyRulingLines()
    {
        (PageContentClass cls, _) = PageClassifier.Decide(S(textRuns: 60, coverage: 0.15, ruling: 30), imageCount: 0);
        Assert.Equal(PageContentClass.TableOrComplexLayout, cls);
    }

    [Fact]
    public void TableOrComplex_WhenTextWithStrongColumnAlignment()
    {
        (PageContentClass cls, _) = PageClassifier.Decide(S(textRuns: 60, coverage: 0.15, columns: 0.8), imageCount: 0);
        Assert.Equal(PageContentClass.TableOrComplexLayout, cls);
    }

    [Fact]
    public void Empty_WhenPositivelyBlank()
    {
        (PageContentClass cls, double conf) = PageClassifier.Decide(S(), imageCount: 0);
        Assert.Equal(PageContentClass.Empty, cls);
        Assert.Equal(1.0, conf, 3);
    }

    [Fact]
    public void FaintScan_NotEmpty_LowConfidence()
    {
        // nothing extractable but a non-dominant image present → escalate, never Empty
        (PageContentClass cls, double conf) = PageClassifier.Decide(S(), imageCount: 1);
        Assert.Equal(PageContentClass.ScannedImageOnly, cls);
        Assert.True(conf < 0.6);
    }

    [Fact]
    public void VectorOnlyPage_NotEmpty()
    {
        (PageContentClass cls, _) = PageClassifier.Decide(S(ruling: 12), imageCount: 0);
        Assert.NotEqual(PageContentClass.Empty, cls);
    }
}
