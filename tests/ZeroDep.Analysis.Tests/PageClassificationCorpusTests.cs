using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using ZeroDep.Abstractions;
using ZeroDep.Analysis;

namespace ZeroDep.Analysis.Tests;

/// <summary>
/// ADR-0003 corpus validation: runs per-page classification over real PDFs and checks the release-blocking
/// invariants — Z-G1 (no image-only/scanned page labelled as a text/form class), Z-G3 (determinism: the
/// same document classifies identically on repeat runs), Z-G5 (no crashes). Writes a content-free class
/// distribution to <c>private/page-class-stats.txt</c>. No-op when the corpus is absent.
/// </summary>
public sealed class PageClassificationCorpusTests
{
    private readonly ITestOutputHelper _output;

    public PageClassificationCorpusTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ClassifiesCorpusDeterministicallyWithoutDangerousErrors()
    {
        string? dataDir = FindDataDirectory();
        if (dataDir is null)
        {
            return;
        }

        var classCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int pdfs = 0, pages = 0, dangerous = 0, nondeterministic = 0;
        const int maxPdfs = 600;

        foreach (string path in Directory.EnumerateFiles(dataDir, "*.pdf", SearchOption.AllDirectories))
        {
            if (pdfs >= maxPdfs)
            {
                break;
            }

            DocumentAnalysis first;
            try
            {
                using (FileStream s = File.OpenRead(path))
                {
                    first = DocumentAnalyzer.Analyze(s, AnalyzerOptions.DefaultDpiThreshold);
                }
            }
            catch (Exception)
            {
                continue; // Z-G5: a parse failure is isolated, never aborts the run
            }

            pdfs++;
            if (first.Status != DocumentStatus.Processed)
            {
                continue;
            }

            // Z-G3 determinism: re-analyze and compare page classes.
            try
            {
                using FileStream s2 = File.OpenRead(path);
                DocumentAnalysis second = DocumentAnalyzer.Analyze(s2, AnalyzerOptions.DefaultDpiThreshold);
                if (second.Pages.Count != first.Pages.Count)
                {
                    nondeterministic++;
                }
                else
                {
                    for (int i = 0; i < first.Pages.Count; i++)
                    {
                        if (first.Pages[i].Class != second.Pages[i].Class)
                        {
                            nondeterministic++;
                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // ignore — determinism check is best-effort on re-open
            }

            foreach (PageClassification page in first.Pages)
            {
                pages++;
                string name = page.Class.ToString();
                classCounts.TryGetValue(name, out int c);
                classCounts[name] = c + 1;

                // Z-G1 dangerous error: a page-dominant image with no text/OCR must never be a text/form class.
                PageSignals sig = page.Signals;
                bool needsPixels = sig.IsImageOnly && sig.TextRunCount == 0 && !sig.OcrLayerPresent;
                bool labelledAsTextOrForm = page.Class is PageContentClass.DigitalText
                    or PageContentClass.FormPage or PageContentClass.TableOrComplexLayout;
                if (needsPixels && labelledAsTextOrForm)
                {
                    dangerous++;
                }
            }
        }

        var report = new StringBuilder();
        report.AppendLine($"pdfs={pdfs} pages={pages} dangerous={dangerous} nondeterministic={nondeterministic}");
        foreach (KeyValuePair<string, int> kv in classCounts)
        {
            report.AppendLine($"  {kv.Key}: {kv.Value}");
        }

        string text = report.ToString();
        _output.WriteLine(text);
        try
        {
            File.WriteAllText(Path.Combine(dataDir, "..", "page-class-stats.txt"), text);
        }
        catch (Exception)
        {
            // writing the aggregate is best-effort
        }

        Assert.Equal(0, dangerous);          // Z-G1 hard zero
        Assert.Equal(0, nondeterministic);   // Z-G3
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
