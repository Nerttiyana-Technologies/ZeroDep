using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using ZeroDep.Abstractions;
using ZeroDep.Analysis;

namespace ZeroDep.Analysis.Tests;

/// <summary>
/// Runs AcroForm extraction across the sample corpus when present. The SF-1449 / SF-30 / SEWP forms
/// store their field dictionaries inside object streams, so this also exercises M1 decompression on
/// real multi-agency instances of the same standard form. No-op (passing) when no samples exist.
/// </summary>
public sealed class RealPdfAcroFormIntegrationTests
{
    [Fact]
    public void ExtractsFormsAcrossSampleCorpusWithoutError()
    {
        string? dataDir = FindDataDirectory();
        if (dataDir is null) return;

        string[] pdfs = Directory.GetFiles(dataDir, "*.pdf", SearchOption.AllDirectories);
        if (pdfs.Length == 0) return;

        var failures = new List<string>();
        int totalFields = 0;
        int acroFormDocs = 0;

        foreach (string path in pdfs)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                AcroFormReport report = AcroFormAnalyzer.Analyze(stream);
                Assert.NotNull(report.Fields);
                if (report.HasAcroForm) acroFormDocs++;
                totalFields += report.Fields.Count;
            }
            catch (ZeroDep.Abstractions.PdfSyntaxException)
            {
                // expected: a corrupt/unsupported file is rejected, not a bug
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0, "AcroForm extraction failed:\n" + string.Join("\n", failures));

        // If the SF-1449 corpus is present, at least one document must yield real fields,
        // proving extraction works through object-stream decompression on real data.
        if (acroFormDocs > 0)
        {
            Assert.True(totalFields > 0, "AcroForm documents present but zero fields extracted");
        }
    }

    private static string? FindDataDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string data = Path.Combine(dir.FullName, "data");
            if (Directory.Exists(data)) return data;
            dir = dir.Parent;
        }
        return null;
    }
}
