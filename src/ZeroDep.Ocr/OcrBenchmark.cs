using System;
using System.Collections.Generic;

namespace ZeroDep.Ocr;

/// <summary>One scored benchmark sample: an OCR hypothesis against its ground-truth reference.</summary>
public sealed class OcrBenchmarkSample
{
    /// <summary>A language/group tag for the per-language breakdown (e.g. <c>eng</c>, <c>zh</c>).</summary>
    public string Language { get; init; } = string.Empty;

    /// <summary>The ground-truth text.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>The OCR output to score.</summary>
    public string Hypothesis { get; init; } = string.Empty;

    /// <summary>The mean confidence (0–1) the engine reported for the hypothesis.</summary>
    public double Confidence { get; init; }
}

/// <summary>CER/WER for one language group.</summary>
public sealed class LanguageScore
{
    /// <summary>The language tag.</summary>
    public string Language { get; init; } = string.Empty;

    /// <summary>The number of samples in the group.</summary>
    public int Samples { get; init; }

    /// <summary>Macro-averaged Character Error Rate (mean of per-sample CER).</summary>
    public double Cer { get; init; }

    /// <summary>Macro-averaged Word Error Rate (mean of per-sample WER).</summary>
    public double Wer { get; init; }

    /// <summary>Micro-averaged (length-weighted) Character Error Rate: Σ edits ÷ Σ reference chars.</summary>
    public double MicroCer { get; init; }

    /// <summary>Micro-averaged (length-weighted) Word Error Rate: Σ edits ÷ Σ reference words.</summary>
    public double MicroWer { get; init; }
}

/// <summary>Error rate within a confidence band — used to verify the engine's confidence is calibrated.</summary>
public sealed class CalibrationBucket
{
    /// <summary>The confidence band label (e.g. <c>0.9–1.0</c>).</summary>
    public string Band { get; init; } = string.Empty;

    /// <summary>The number of samples whose confidence fell in the band.</summary>
    public int Samples { get; init; }

    /// <summary>Macro-averaged Character Error Rate for samples in the band.</summary>
    public double Cer { get; init; }

    /// <summary>Micro-averaged (length-weighted) Character Error Rate for samples in the band.</summary>
    public double MicroCer { get; init; }
}

/// <summary>The result of an accuracy benchmark over a labeled set.</summary>
public sealed class OcrBenchmarkReport
{
    /// <summary>Total samples scored.</summary>
    public int Samples { get; init; }

    /// <summary>Macro-averaged Character Error Rate across all samples (each sample weighted equally).</summary>
    public double Cer { get; init; }

    /// <summary>Macro-averaged Word Error Rate across all samples.</summary>
    public double Wer { get; init; }

    /// <summary>
    /// Micro-averaged (length-weighted) Character Error Rate: Σ edits ÷ Σ reference chars over all
    /// samples. The standard corpus-level OCR metric — long passages dominate, short fragments
    /// contribute proportionally. This is the headline accuracy-gate number.
    /// </summary>
    public double MicroCer { get; init; }

    /// <summary>Micro-averaged (length-weighted) Word Error Rate: Σ edits ÷ Σ reference words.</summary>
    public double MicroWer { get; init; }

    /// <summary>Per-language CER/WER.</summary>
    public IReadOnlyList<LanguageScore> ByLanguage { get; init; } = Array.Empty<LanguageScore>();

    /// <summary>Error rate bucketed by reported confidence (should decrease as confidence rises).</summary>
    public IReadOnlyList<CalibrationBucket> Calibration { get; init; } = Array.Empty<CalibrationBucket>();
}

/// <summary>
/// Computes an accuracy benchmark (ADR-0002 §8.1) over scored OCR samples: aggregate and per-language
/// CER/WER, plus confidence calibration. Engine- and dataset-agnostic — feed it samples produced by
/// any <see cref="IOcrEngine"/> against any labeled set.
/// </summary>
public static class OcrBenchmark
{
    private static readonly (double Lo, double Hi, string Band)[] Bands =
    {
        (0.00, 0.50, "0.0-0.5"),
        (0.50, 0.70, "0.5-0.7"),
        (0.70, 0.90, "0.7-0.9"),
        (0.90, 1.01, "0.9-1.0"),
    };

    /// <summary>Scores a set of samples into a report.</summary>
    public static OcrBenchmarkReport Evaluate(IReadOnlyList<OcrBenchmarkSample> samples)
    {
        if (samples is null)
        {
            throw new ArgumentNullException(nameof(samples));
        }

        if (samples.Count == 0)
        {
            return new OcrBenchmarkReport();
        }

        var total = new Acc();
        var langs = new Dictionary<string, Acc>(StringComparer.Ordinal);
        var bandAcc = new Acc[Bands.Length];
        for (int i = 0; i < bandAcc.Length; i++)
        {
            bandAcc[i] = new Acc();
        }

        foreach (OcrBenchmarkSample sample in samples)
        {
            (int charDist, int charLen) = OcrAccuracy.CharacterDistance(sample.Reference, sample.Hypothesis);
            (int wordDist, int wordLen) = OcrAccuracy.WordDistance(sample.Reference, sample.Hypothesis);
            double cer = charLen == 0 ? (charDist == 0 ? 0.0 : 1.0) : (double)charDist / charLen;
            double wer = wordLen == 0 ? (wordDist == 0 ? 0.0 : 1.0) : (double)wordDist / wordLen;

            total.Add(cer, wer, charDist, charLen, wordDist, wordLen);

            string lang = sample.Language ?? string.Empty;
            if (!langs.TryGetValue(lang, out Acc? acc))
            {
                acc = new Acc();
                langs[lang] = acc;
            }

            acc.Add(cer, wer, charDist, charLen, wordDist, wordLen);

            for (int b = 0; b < Bands.Length; b++)
            {
                if (sample.Confidence >= Bands[b].Lo && sample.Confidence < Bands[b].Hi)
                {
                    bandAcc[b].Add(cer, wer, charDist, charLen, wordDist, wordLen);
                    break;
                }
            }
        }

        var byLanguage = new List<LanguageScore>();
        foreach (KeyValuePair<string, Acc> kv in langs)
        {
            byLanguage.Add(new LanguageScore
            {
                Language = kv.Key,
                Samples = kv.Value.Count,
                Cer = kv.Value.MeanCer,
                Wer = kv.Value.MeanWer,
                MicroCer = kv.Value.MicroCer,
                MicroWer = kv.Value.MicroWer,
            });
        }

        var calibration = new List<CalibrationBucket>();
        for (int b = 0; b < Bands.Length; b++)
        {
            calibration.Add(new CalibrationBucket
            {
                Band = Bands[b].Band,
                Samples = bandAcc[b].Count,
                Cer = bandAcc[b].MeanCer,
                MicroCer = bandAcc[b].MicroCer,
            });
        }

        return new OcrBenchmarkReport
        {
            Samples = samples.Count,
            Cer = total.MeanCer,
            Wer = total.MeanWer,
            MicroCer = total.MicroCer,
            MicroWer = total.MicroWer,
            ByLanguage = byLanguage,
            Calibration = calibration,
        };
    }

    private sealed class Acc
    {
        private double _cer;
        private double _wer;
        private long _charDist;
        private long _charLen;
        private long _wordDist;
        private long _wordLen;

        public int Count { get; private set; }

        public double MeanCer => Count == 0 ? 0 : _cer / Count;

        public double MeanWer => Count == 0 ? 0 : _wer / Count;

        public double MicroCer => _charLen == 0 ? 0 : (double)_charDist / _charLen;

        public double MicroWer => _wordLen == 0 ? 0 : (double)_wordDist / _wordLen;

        public void Add(double cer, double wer, int charDist, int charLen, int wordDist, int wordLen)
        {
            _cer += cer;
            _wer += wer;
            _charDist += charDist;
            _charLen += charLen;
            _wordDist += wordDist;
            _wordLen += wordLen;
            Count++;
        }
    }
}
