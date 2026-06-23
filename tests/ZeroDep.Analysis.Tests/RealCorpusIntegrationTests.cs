using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using ZeroDep.Abstractions;
using ZeroDep.Analysis;

namespace ZeroDep.Analysis.Tests;

/// <summary>
/// Over every sample PDF under data/, the unified analyzer must either Process or cleanly Reject —
/// never throw an unexpected exception. This validates the M1.5 reject-don't-crash contract on real
/// documents (including any corrupt ones). No-op (passing) when no samples are present.
/// </summary>
public sealed class RealCorpusIntegrationTests
{
    [Fact]
    public void EveryDocumentIsProcessedOrCleanlyRejected()
    {
        string? dataDir = FindDataDirectory();
        if (dataDir is null) return;

        string[] pdfs = Directory.GetFiles(dataDir, "*.pdf", SearchOption.AllDirectories);
        if (pdfs.Length == 0) return;

        var bugs = new List<string>();
        int processed = 0, rejected = 0;

        foreach (string path in pdfs)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                DocumentAnalysis a = DocumentAnalyzer.Analyze(stream, AnalyzerOptions.DefaultDpiThreshold);

                if (a.Status == DocumentStatus.Rejected)
                {
                    rejected++;
                    if (a.Rejection is null) bugs.Add($"{Path.GetFileName(path)}: Rejected without a reason");
                }
                else
                {
                    processed++;
                    if (a.PageCount <= 0) bugs.Add($"{Path.GetFileName(path)}: Processed with no pages");
                }
            }
            catch (Exception ex)
            {
                // The unified analyzer is contractually no-throw; any exception is a real bug.
                bugs.Add($"{Path.GetFileName(path)}: unexpected {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(bugs.Count == 0, "Contract violations:\n" + string.Join("\n", bugs));
        Assert.True(processed + rejected == pdfs.Length);
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
