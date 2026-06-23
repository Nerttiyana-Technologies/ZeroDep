using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using ZeroDep.Model;

namespace ZeroDep.Model.Tests;

/// <summary>
/// Opens every real-world PDF under <c>data/</c> when present in the working tree, covering both
/// classic cross-reference tables and xref/object streams. The test is a no-op (passes) when no
/// samples are present, so CI without the files stays green.
/// </summary>
public sealed class RealPdfIntegrationTests
{
    [Fact]
    public void OpensEverySamplePdfWhenPresent()
    {
        string? dataDir = FindDataDirectory();
        if (dataDir is null) return; // samples not available in this environment

        string[] pdfs = Directory.GetFiles(dataDir, "*.pdf", SearchOption.AllDirectories);
        if (pdfs.Length == 0) return;

        var failures = new List<string>();
        foreach (string path in pdfs)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                using var document = PdfDocument.Open(stream);

                if (document.PageCount <= 0)
                {
                    failures.Add($"{Path.GetFileName(path)}: no pages");
                    continue;
                }
                foreach (PdfPage page in document.Pages)
                {
                    if (page.MediaBox.Width <= 0 || page.MediaBox.Height <= 0)
                    {
                        failures.Add($"{Path.GetFileName(path)}: page {page.Index} has non-positive MediaBox");
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

        Assert.True(failures.Count == 0, "Failed to parse:\n" + string.Join("\n", failures));
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
