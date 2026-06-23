using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using ZeroDep.Abstractions;
using ZeroDep.Analysis;

namespace ZeroDep.Analysis.Tests;

/// <summary>
/// Runs the DPI analyzer across every sample PDF under <c>data/</c> when present, exercising the
/// content interpreter and image metrics on real documents. A no-op (passing) when none are present.
/// </summary>
public sealed class RealPdfDpiIntegrationTests
{
    [Fact]
    public void AnalyzesEverySamplePdfWithoutError()
    {
        string? dataDir = FindDataDirectory();
        if (dataDir is null) return;

        string[] pdfs = Directory.GetFiles(dataDir, "*.pdf", SearchOption.AllDirectories);
        if (pdfs.Length == 0) return;

        var failures = new List<string>();
        foreach (string path in pdfs)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                IReadOnlyList<ImageDpiInfo> images = DpiAnalyzer.Analyze(stream, threshold: 150);

                // Sanity: every reported image has positive pixel dimensions.
                foreach (ImageDpiInfo image in images)
                {
                    if (image.PixelWidth < 0 || image.PixelHeight < 0)
                    {
                        failures.Add($"{Path.GetFileName(path)}: negative pixel dimensions");
                        break;
                    }
                }
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

        Assert.True(failures.Count == 0, "DPI analysis failed:\n" + string.Join("\n", failures));
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
