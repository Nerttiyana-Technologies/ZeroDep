using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using ZeroDep.Abstractions;
using ZeroDep.Analysis;

namespace ZeroDep.Analysis.Tests;

/// <summary>
/// ADR-0007 Z-G6 calibration + T4 harness: runs the analyzer over the real corpus and writes the distribution
/// of <c>TextDecodeConfidence</c> for fast-lane-candidate pages (DigitalText / TableOrComplexLayout) to
/// <c>private/text-decode-stats.txt</c>, so the penalty weight and a recommended consumer threshold can be
/// calibrated against the bimodal good-vs-garbage split. Also asserts the score is deterministic across
/// repeat runs (T4) and that the run never crashes (isolated faults). No-op when the corpus is absent.
/// </summary>
public sealed class TextDecodeConfidenceCorpusTests
{
    private const int MaxPdfs = 600;

    private readonly ITestOutputHelper _output;

    public TextDecodeConfidenceCorpusTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ScoresCorpusDeterministicallyAndDumpsDistribution()
    {
        string? dataDir = FindDataDirectory();
        if (dataDir is null)
        {
            return;
        }

        // 20 buckets over [0,1].
        var fastLaneHist = new int[21];
        var allTextHist = new int[21];
        int pdfs = 0, fastLanePages = 0, textPages = 0, nondeterministic = 0;
        int lowTrustFastLane = 0; // < 0.75 — candidate escalations

        foreach (string path in Directory.EnumerateFiles(dataDir, "*.pdf", SearchOption.AllDirectories))
        {
            if (pdfs >= MaxPdfs)
            {
                break;
            }

            DocumentAnalysis first;
            try
            {
                using FileStream s = File.OpenRead(path);
                first = DocumentAnalyzer.Analyze(s, AnalyzerOptions.DefaultDpiThreshold);
            }
            catch (Exception)
            {
                continue; // isolated fault
            }

            pdfs++;
            if (first.Status != DocumentStatus.Processed)
            {
                continue;
            }

            DocumentAnalysis? second = null;
            try
            {
                using FileStream s2 = File.OpenRead(path);
                second = DocumentAnalyzer.Analyze(s2, AnalyzerOptions.DefaultDpiThreshold);
            }
            catch (Exception)
            {
                // determinism check best-effort
            }

            for (int i = 0; i < first.Pages.Count; i++)
            {
                PageClassification page = first.Pages[i];
                double score = page.Signals.TextDecodeConfidence;

                if (second is not null && i < second.Pages.Count
                    && second.Pages[i].Signals.TextDecodeConfidence != score)
                {
                    nondeterministic++;
                }

                bool hasText = page.Signals.TextRunCount > 0;
                if (hasText)
                {
                    textPages++;
                    allTextHist[Bucket(score)]++;
                }

                if (page.Class is PageContentClass.DigitalText or PageContentClass.TableOrComplexLayout)
                {
                    fastLanePages++;
                    fastLaneHist[Bucket(score)]++;
                    if (score < 0.75)
                    {
                        lowTrustFastLane++;
                    }
                }
            }
        }

        var report = new StringBuilder();
        report.AppendLine($"pdfs={pdfs} textPages={textPages} fastLanePages={fastLanePages} "
            + $"lowTrustFastLane(<0.75)={lowTrustFastLane} nondeterministic={nondeterministic}");
        report.AppendLine("bucket\tfastLane\tallText");
        for (int b = 0; b <= 20; b++)
        {
            report.AppendLine($"{(b / 20.0).ToString("0.00", CultureInfo.InvariantCulture)}\t{fastLaneHist[b]}\t{allTextHist[b]}");
        }

        string text = report.ToString();
        _output.WriteLine(text);
        try
        {
            File.WriteAllText(Path.Combine(dataDir, "..", "text-decode-stats.txt"), text);
        }
        catch (Exception)
        {
            // best-effort
        }

        Assert.Equal(0, nondeterministic); // T4 determinism
    }

    private static int Bucket(double score)
    {
        int b = (int)Math.Round(score * 20, MidpointRounding.AwayFromZero);
        return b < 0 ? 0 : (b > 20 ? 20 : b);
    }

    private static string? FindDataDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string full = Path.Combine(dir.FullName, "private", "data");
            if (Directory.Exists(full))
            {
                return full;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
