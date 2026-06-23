using System;
using System.Collections.Generic;
using System.Globalization;
using ZeroDep;
using ZeroDep.Abstractions;

namespace ZeroDep.Batch;

/// <summary>
/// Folds per-file outcomes into the publishable aggregate statistics (ADR §8.2). Aggregation is
/// order-independent (counts and a sorted DPI multiset), so parallel runs reproduce identical
/// statistics. Output is aggregates and counts only — no content ever enters this stage.
/// </summary>
internal static class Aggregator
{
    public static AggregateStatistics Build(IReadOnlyList<FileOutcome> outcomes, ClassificationThresholds thresholds)
    {
        int total = outcomes.Count;
        int processed = 0;
        int rejected = 0;
        int encryptedUnreadable = 0;
        int encrypted = 0;
        int ocrFiles = 0;
        int formFiles = 0;
        int imagesMeasured = 0;
        int belowThreshold = 0;
        int chosenThreshold = 0;

        var categoryCounts = new Dictionary<DocumentCategory, int>();
        var algorithmCounts = new Dictionary<EncryptionAlgorithm, int>();
        var rejectionCounts = new Dictionary<RejectionReason, int>();
        var dpis = new List<double>();

        foreach (FileOutcome outcome in outcomes)
        {
            switch (outcome.Status)
            {
                case DocumentStatus.Processed:
                    processed++;
                    break;
                case DocumentStatus.Rejected:
                    rejected++;
                    break;
            }

            if (outcome.EncryptedUnreadable)
            {
                encryptedUnreadable++;
            }

            if (outcome.Encrypted)
            {
                encrypted++;
                if (outcome.EncryptionAlgorithm != EncryptionAlgorithm.None)
                {
                    Increment(algorithmCounts, outcome.EncryptionAlgorithm);
                }
            }

            if (outcome.OcrPresent)
            {
                ocrFiles++;
            }

            if (outcome.FormPresent)
            {
                formFiles++;
            }

            Increment(categoryCounts, outcome.Category);

            if (outcome.Status == DocumentStatus.Rejected && outcome.Rejection != RejectionReason.None)
            {
                Increment(rejectionCounts, outcome.Rejection);
            }

            imagesMeasured += outcome.EffectiveDpis.Count;
            belowThreshold += outcome.BelowThresholdCount;
            foreach (double dpi in outcome.EffectiveDpis)
            {
                dpis.Add(dpi);
            }

            if (outcome.Threshold > 0)
            {
                chosenThreshold = outcome.Threshold;
            }
        }

        if (chosenThreshold == 0)
        {
            chosenThreshold = ClassificationThresholdsDefaultThreshold;
        }

        dpis.Sort();

        return new AggregateStatistics
        {
            GeneratedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            EngineVersion = "ZeroDep " + ZeroDepInfo.Version,
            Corpus = new CorpusCounts
            {
                Total = total,
                Processed = processed,
                Rejected = rejected,
                EncryptedUnreadable = encryptedUnreadable,
            },
            Thresholds = thresholds,
            Categories = BuildCategories(categoryCounts, total),
            Dpi = new DpiStatistics
            {
                ImagesMeasured = imagesMeasured,
                BelowThresholdPercent = Percent(belowThreshold, imagesMeasured),
                Threshold = chosenThreshold,
                Min = dpis.Count > 0 ? Round(dpis[0]) : 0,
                Median = Percentile(dpis, 0.50),
                P95 = Percentile(dpis, 0.95),
                Histogram = BuildHistogram(dpis),
            },
            OcrLayerPresentPercent = Percent(ocrFiles, processed),
            FormPresentPercent = Percent(formFiles, processed),
            Encryption = new EncryptionStatistics
            {
                EncryptedPercent = Percent(encrypted, total),
                ByAlgorithm = BuildAlgorithms(algorithmCounts),
            },
            Rejections = BuildRejections(rejectionCounts),
        };
    }

    private const int ClassificationThresholdsDefaultThreshold = 150;

    private static void Increment<TKey>(Dictionary<TKey, int> map, TKey key)
        where TKey : notnull
    {
        map.TryGetValue(key, out int count);
        map[key] = count + 1;
    }

    private static IReadOnlyList<CategoryCount> BuildCategories(Dictionary<DocumentCategory, int> counts, int total)
    {
        var list = new List<CategoryCount>();
        foreach (DocumentCategory category in (DocumentCategory[])Enum.GetValues(typeof(DocumentCategory)))
        {
            if (counts.TryGetValue(category, out int count) && count > 0)
            {
                list.Add(new CategoryCount
                {
                    Category = category.ToString(),
                    Count = count,
                    Percent = Percent(count, total),
                });
            }
        }

        return list;
    }

    private static IReadOnlyList<AlgorithmCount> BuildAlgorithms(Dictionary<EncryptionAlgorithm, int> counts)
    {
        var list = new List<AlgorithmCount>();
        foreach (EncryptionAlgorithm algorithm in (EncryptionAlgorithm[])Enum.GetValues(typeof(EncryptionAlgorithm)))
        {
            if (counts.TryGetValue(algorithm, out int count) && count > 0)
            {
                list.Add(new AlgorithmCount { Algorithm = algorithm.ToString(), Count = count });
            }
        }

        return list;
    }

    private static IReadOnlyList<RejectionCount> BuildRejections(Dictionary<RejectionReason, int> counts)
    {
        var list = new List<RejectionCount>();
        foreach (RejectionReason reason in (RejectionReason[])Enum.GetValues(typeof(RejectionReason)))
        {
            if (counts.TryGetValue(reason, out int count) && count > 0)
            {
                list.Add(new RejectionCount { Reason = reason.ToString(), Count = count });
            }
        }

        return list;
    }

    private static IReadOnlyList<DpiBucket> BuildHistogram(IReadOnlyList<double> sortedDpis)
    {
        int b0 = 0;
        int b1 = 0;
        int b2 = 0;
        int b3 = 0;
        foreach (double dpi in sortedDpis)
        {
            if (dpi < 150)
            {
                b0++;
            }
            else if (dpi < 300)
            {
                b1++;
            }
            else if (dpi < 600)
            {
                b2++;
            }
            else
            {
                b3++;
            }
        }

        return new[]
        {
            new DpiBucket { Bucket = "<150", Count = b0 },
            new DpiBucket { Bucket = "150-299", Count = b1 },
            new DpiBucket { Bucket = "300-599", Count = b2 },
            new DpiBucket { Bucket = ">=600", Count = b3 },
        };
    }

    private static double Percentile(IReadOnlyList<double> sorted, double fraction)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        int rank = (int)Math.Ceiling(fraction * sorted.Count) - 1;
        if (rank < 0)
        {
            rank = 0;
        }

        if (rank >= sorted.Count)
        {
            rank = sorted.Count - 1;
        }

        return Round(sorted[rank]);
    }

    private static double Percent(int part, int whole)
        => whole <= 0 ? 0 : Math.Round(part * 100.0 / whole, 1);

    private static double Round(double value) => Math.Round(value, 1);
}
