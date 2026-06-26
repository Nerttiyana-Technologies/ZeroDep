using System;
using System.Collections.Generic;
using ZeroDep.Abstractions;
using ZeroDep.Model;

namespace ZeroDep.Analysis;

/// <summary>
/// Per-page structural classification (ADR-0003): computes <see cref="PageSignals"/> from the already-
/// gathered per-page data (text runs, images, widgets, ruling/font structure) in one pass, then assigns a
/// <see cref="PageContentClass"/> with confidence using the deterministic gather-then-classify rules
/// (§2.2). Bias is toward the safer (more-work) class on ambiguity; ratio signals are rounded for
/// cross-target determinism.
/// </summary>
internal static class PageClassifier
{
    private const double ImageDominantFraction = 0.80;
    private const int RulingThreshold = 12;       // ruling lines to flag table/complex (P5 — tunable)
    private const double ColumnThreshold = 0.60;  // column-alignment score to flag table/complex (P5)
    private const double LowCoverage = 0.02;      // sparse marks → lower confidence

    public static IReadOnlyList<PageClassification> Classify(
        PdfDocument document,
        IReadOnlyList<TextRunInfo> textRuns,
        IReadOnlyList<ImageDpiInfo> images,
        AcroFormReport form,
        IReadOnlyList<PageStructure> pageStructures)
    {
        var runsByPage = Group(textRuns, r => r.PageIndex);
        var imagesByPage = Group(images, i => i.PageIndex);
        var fieldsByPage = new Dictionary<int, List<FormFieldInfo>>();
        foreach (FormFieldInfo field in form.Fields)
        {
            if (field.PageIndex is int p)
            {
                Add(fieldsByPage, p, field);
            }
        }

        var structureByPage = new Dictionary<int, PageStructure>();
        foreach (PageStructure s in pageStructures)
        {
            structureByPage[s.PageIndex] = s;
        }

        var result = new List<PageClassification>(document.PageCount);
        foreach (PdfPage page in document.Pages)
        {
            int p = page.Index;
            double pageArea = page.MediaBox.Width * page.MediaBox.Height;
            runsByPage.TryGetValue(p, out List<TextRunInfo>? runs);
            imagesByPage.TryGetValue(p, out List<ImageDpiInfo>? pageImages);
            fieldsByPage.TryGetValue(p, out List<FormFieldInfo>? fields);
            structureByPage.TryGetValue(p, out PageStructure structure);

            PageSignals signals = BuildSignals(runs, pageImages, fields, structure, pageArea);
            (PageContentClass cls, double confidence) = Decide(signals, pageImages?.Count ?? 0);

            result.Add(new PageClassification
            {
                PageIndex = p,
                Class = cls,
                Confidence = Math.Round(confidence, 2, MidpointRounding.AwayFromZero),
                Signals = signals,
            });
        }

        return result;
    }

    private static PageSignals BuildSignals(
        List<TextRunInfo>? runs, List<ImageDpiInfo>? images, List<FormFieldInfo>? fields, PageStructure structure, double pageArea)
    {
        int textRunCount = 0;
        bool ocrLayerPresent = false;
        double textAreaSum = 0;
        if (runs is not null)
        {
            foreach (TextRunInfo run in runs)
            {
                if (run.IsOcrLayer || run.Source == TextSource.OcrGenerated)
                {
                    ocrLayerPresent = true;
                    continue;
                }

                textRunCount++;
                textAreaSum += Math.Abs(run.Width) * Math.Abs(run.FontSize);
            }
        }

        double maxImageFraction = 0;
        double minDpi = 0;
        if (images is not null && images.Count > 0)
        {
            minDpi = double.MaxValue;
            foreach (ImageDpiInfo img in images)
            {
                if (img.PageAreaFraction > maxImageFraction)
                {
                    maxImageFraction = img.PageAreaFraction;
                }

                if (img.EffectiveDpi > 0 && img.EffectiveDpi < minDpi)
                {
                    minDpi = img.EffectiveDpi;
                }
            }

            if (minDpi == double.MaxValue)
            {
                minDpi = 0;
            }
        }

        double coverage = pageArea > 0 ? textAreaSum / pageArea : 0;
        coverage = coverage < 0 ? 0 : (coverage > 1 ? 1 : coverage);

        return new PageSignals
        {
            TextRunCount = textRunCount,
            TextCoverageFraction = Math.Round(coverage, 4, MidpointRounding.AwayFromZero),
            HasAcroFormWidgets = fields is { Count: > 0 },
            WidgetCount = fields?.Count ?? 0,
            IsImageOnly = maxImageFraction >= ImageDominantFraction,
            OcrLayerPresent = ocrLayerPresent,
            MinImageDpi = Math.Round(minDpi, 2, MidpointRounding.AwayFromZero),
            RulingLineCount = structure.RulingLineCount,
            ColumnAlignmentScore = ColumnScore(runs),
            FontDistinctCount = structure.FontDistinctCount,
        };
    }

    /// <summary>The deterministic gather-then-classify decision (ADR-0003 §2.2), exposed for unit tests.</summary>
    internal static (PageContentClass Class, double Confidence) Decide(PageSignals s, int imageCount)
    {
        bool hasRealText = s.TextRunCount > 0 && s.TextCoverageFraction > 0;
        // "Real text" for routing also requires a minimum of substance; approximate via run count.
        bool substantialText = s.TextRunCount >= 3 || s.TextCoverageFraction >= 0.01;
        bool tableComplex = s.RulingLineCount >= RulingThreshold || s.ColumnAlignmentScore >= ColumnThreshold;

        if (s.IsImageOnly)
        {
            if (substantialText)
            {
                return (PageContentClass.Mixed, 0.70);
            }

            return s.OcrLayerPresent
                ? (PageContentClass.ScannedWithOcr, 0.90)
                : (PageContentClass.ScannedImageOnly, 0.90);
        }

        if (s.HasAcroFormWidgets)
        {
            // FormPage only when non-widget content is negligible; a non-widget table → Mixed (escalate).
            return tableComplex ? (PageContentClass.Mixed, 0.70) : (PageContentClass.FormPage, 0.85);
        }

        if (tableComplex && substantialText)
        {
            return (PageContentClass.TableOrComplexLayout, 0.70);
        }

        if (substantialText)
        {
            return (PageContentClass.DigitalText, s.TextCoverageFraction < LowCoverage ? 0.55 : 0.90);
        }

        // No real text, no widgets, no dominant image.
        if (s.TextRunCount == 0 && imageCount == 0 && s.RulingLineCount == 0)
        {
            return (PageContentClass.Empty, 1.00); // positively blank
        }

        if (imageCount > 0)
        {
            return (PageContentClass.ScannedImageOnly, 0.40); // figures / non-dominant images → escalate
        }

        if (s.RulingLineCount > 0)
        {
            return (PageContentClass.TableOrComplexLayout, 0.50); // vector-only diagram → escalate
        }

        return (hasRealText ? PageContentClass.DigitalText : PageContentClass.Empty, 0.40);
    }

    // Aligned-run density: fraction of runs whose left edge clusters into >=2 strong columns. Deterministic
    // (integer 3-pt bucketing + fixed rounding). Returns 0 for a single left-aligned flow.
    private static double ColumnScore(List<TextRunInfo>? runs)
    {
        if (runs is null || runs.Count < 6)
        {
            return 0;
        }

        var buckets = new Dictionary<int, int>();
        int total = 0;
        foreach (TextRunInfo run in runs)
        {
            if (run.IsOcrLayer || run.Source == TextSource.OcrGenerated)
            {
                continue;
            }

            int key = (int)Math.Round(run.X / 3.0, MidpointRounding.AwayFromZero);
            buckets.TryGetValue(key, out int c);
            buckets[key] = c + 1;
            total++;
        }

        if (total < 6)
        {
            return 0;
        }

        int strongColumns = 0;
        int alignedRuns = 0;
        foreach (KeyValuePair<int, int> b in buckets)
        {
            if (b.Value >= 3)
            {
                strongColumns++;
                alignedRuns += b.Value;
            }
        }

        if (strongColumns < 2)
        {
            return 0;
        }

        return Math.Round((double)alignedRuns / total, 4, MidpointRounding.AwayFromZero);
    }

    private static Dictionary<int, List<T>> Group<T>(IReadOnlyList<T> items, Func<T, int> key)
    {
        var map = new Dictionary<int, List<T>>();
        foreach (T item in items)
        {
            Add(map, key(item), item);
        }

        return map;
    }

    private static void Add<T>(Dictionary<int, List<T>> map, int key, T item)
    {
        if (!map.TryGetValue(key, out List<T>? list))
        {
            list = new List<T>();
            map[key] = list;
        }

        list.Add(item);
    }
}
