using ZeroDep.Json;

namespace ZeroDep.Batch;

/// <summary>
/// Serializes <see cref="AggregateStatistics"/> to JSON using the shared pure-BCL writer. The writer
/// emits only what the type exposes, so the §8.2 privacy boundary holds at serialization too.
/// </summary>
internal static class AggregateStatisticsJson
{
    public static string Write(AggregateStatistics stats, bool indent = false)
    {
        var w = new JsonWriter(indent);
        w.BeginObject();
        w.Property("schemaVersion").Value(stats.SchemaVersion);
        w.Property("generatedUtc").Value(stats.GeneratedUtc);
        w.Property("engineVersion").Value(stats.EngineVersion);

        w.Property("corpus").BeginObject();
        w.Property("total").Value(stats.Corpus.Total);
        w.Property("processed").Value(stats.Corpus.Processed);
        w.Property("rejected").Value(stats.Corpus.Rejected);
        w.Property("encryptedUnreadable").Value(stats.Corpus.EncryptedUnreadable);
        w.EndObject();

        w.Property("classificationThresholds").BeginObject();
        w.Property("imageAreaPct").Value(stats.Thresholds.ImageAreaPercent);
        w.Property("minTextChars").Value(stats.Thresholds.MinTextChars);
        w.EndObject();

        w.Property("categories").BeginArray();
        foreach (CategoryCount category in stats.Categories)
        {
            w.BeginObject();
            w.Property("category").Value(category.Category);
            w.Property("count").Value(category.Count);
            w.Property("pct").Value(category.Percent);
            w.EndObject();
        }

        w.EndArray();

        DpiStatistics dpi = stats.Dpi;
        w.Property("dpi").BeginObject();
        w.Property("imagesMeasured").Value(dpi.ImagesMeasured);
        w.Property("belowThresholdPct").Value(dpi.BelowThresholdPercent);
        w.Property("threshold").Value(dpi.Threshold);
        w.Property("min").Value(dpi.Min);
        w.Property("median").Value(dpi.Median);
        w.Property("p95").Value(dpi.P95);
        w.Property("histogram").BeginArray();
        foreach (DpiBucket bucket in dpi.Histogram)
        {
            w.BeginObject();
            w.Property("bucket").Value(bucket.Bucket);
            w.Property("count").Value(bucket.Count);
            w.EndObject();
        }

        w.EndArray();
        w.EndObject();

        w.Property("ocrLayerPresentPct").Value(stats.OcrLayerPresentPercent);
        w.Property("formPresentPct").Value(stats.FormPresentPercent);

        w.Property("encryption").BeginObject();
        w.Property("encryptedPct").Value(stats.Encryption.EncryptedPercent);
        w.Property("byAlgorithm").BeginArray();
        foreach (AlgorithmCount algorithm in stats.Encryption.ByAlgorithm)
        {
            w.BeginObject();
            w.Property("algorithm").Value(algorithm.Algorithm);
            w.Property("count").Value(algorithm.Count);
            w.EndObject();
        }

        w.EndArray();
        w.EndObject();

        w.Property("rejections").BeginArray();
        foreach (RejectionCount rejection in stats.Rejections)
        {
            w.BeginObject();
            w.Property("reason").Value(rejection.Reason);
            w.Property("count").Value(rejection.Count);
            w.EndObject();
        }

        w.EndArray();

        w.EndObject();
        return w.ToString();
    }
}
