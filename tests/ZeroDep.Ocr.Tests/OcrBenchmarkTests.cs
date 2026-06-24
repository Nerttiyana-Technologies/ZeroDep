using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZeroDep.Ocr;

namespace ZeroDep.Ocr.Tests;

/// <summary>The accuracy-benchmark aggregator: aggregate + per-language CER/WER and confidence calibration.</summary>
public sealed class OcrBenchmarkTests
{
    [Fact]
    public void Evaluate_AggregatesAndBreaksDownByLanguage()
    {
        var samples = new List<OcrBenchmarkSample>
        {
            new() { Language = "eng", Reference = "hello", Hypothesis = "hello", Confidence = 0.95 },  // CER 0
            new() { Language = "eng", Reference = "hello", Hypothesis = "hallo", Confidence = 0.6 },   // CER 0.2
            new() { Language = "deu", Reference = "guten tag", Hypothesis = "guten tag", Confidence = 0.92 }, // CER 0
        };

        OcrBenchmarkReport report = OcrBenchmark.Evaluate(samples);

        Assert.Equal(3, report.Samples);
        Assert.Equal((0.0 + 0.2 + 0.0) / 3.0, report.Cer, 6);

        LanguageScore eng = report.ByLanguage.Single(l => l.Language == "eng");
        Assert.Equal(2, eng.Samples);
        Assert.Equal(0.1, eng.Cer, 6);   // mean of 0 and 0.2
        Assert.Equal(0.0, report.ByLanguage.Single(l => l.Language == "deu").Cer, 6);
    }

    [Fact]
    public void Evaluate_CalibrationBucketsByConfidence()
    {
        var samples = new List<OcrBenchmarkSample>
        {
            new() { Reference = "abcde", Hypothesis = "abcde", Confidence = 0.95 },  // high conf, CER 0
            new() { Reference = "abcde", Hypothesis = "xbcde", Confidence = 0.95 },  // high conf, CER 0.2
            new() { Reference = "abcde", Hypothesis = "xyzde", Confidence = 0.4 },   // low conf, CER 0.6 (3 of 5)
        };

        OcrBenchmarkReport report = OcrBenchmark.Evaluate(samples);

        CalibrationBucket high = report.Calibration.Single(b => b.Band == "0.9-1.0");
        CalibrationBucket low = report.Calibration.Single(b => b.Band == "0.0-0.5");
        Assert.Equal(2, high.Samples);
        Assert.Equal(0.1, high.Cer, 6);            // mean of 0 and 0.2
        Assert.Equal(1, low.Samples);
        Assert.Equal(0.6, low.Cer, 6);
        Assert.True(high.Cer < low.Cer);           // calibrated: higher confidence → lower error
    }

    [Fact]
    public void Evaluate_EmptyInput_IsEmptyReport()
        => Assert.Equal(0, OcrBenchmark.Evaluate(new List<OcrBenchmarkSample>()).Samples);
}
