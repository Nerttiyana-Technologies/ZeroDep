using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;
using ZeroDep.Abstractions;
using ZeroDep.Filters.Jpx;

namespace ZeroDep.Analysis.Tests;

/// <summary>
/// Validates the JPEG 2000 codestream parser (Phase 1) against the real corpus: every embedded
/// <c>/JPXDecode</c> image's main header must parse, and its SIZ dimensions must match the PDF's
/// declared size. Prints the structural distribution (wavelet, levels, components, progression) to
/// ground the later decode stages. No-op when the corpus is absent.
/// </summary>
public sealed class JpxCorpusParseTests
{
    private readonly ITestOutputHelper _output;

    public JpxCorpusParseTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ParsesJpxCodestreamHeaders()
    {
        string? dataDir = FindDataDirectory();
        if (dataDir is null)
        {
            return;
        }

        string[] pdfs = Directory.GetFiles(dataDir, "*.pdf", SearchOption.AllDirectories);
        if (pdfs.Length == 0)
        {
            return;
        }

        int jpx = 0, parsed = 0, dimOk = 0, dimMismatch = 0, errors = 0;
        int rev = 0, irrev = 0;
        var levelsHist = new SortedDictionary<int, int>();
        var compHist = new SortedDictionary<int, int>();
        var progHist = new SortedDictionary<int, int>();
        var failures = new List<string>();

        foreach (string path in pdfs)
        {
            IReadOnlyList<PdfImageInfo> images;
            try
            {
                using FileStream stream = File.OpenRead(path);
                images = ImageExtractor.Extract(stream);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (PdfImageInfo image in images)
            {
                if (image.Filter is null || image.Filter.IndexOf("JPXDecode", StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                jpx++;
                if (image.Filter != "JPXDecode")
                {
                    continue;
                }

                try
                {
                    JpxImage img = JpxCodestream.Parse(image.EncodedData);
                    parsed++;

                    if (img.Cod.Reversible)
                    {
                        rev++;
                    }
                    else
                    {
                        irrev++;
                    }

                    Bump(levelsHist, img.Cod.DecompositionLevels);
                    Bump(compHist, img.Siz.Components.Length);
                    Bump(progHist, img.Cod.Progression);

                    if (img.Siz.Width == image.DeclaredWidth && img.Siz.Height == image.DeclaredHeight)
                    {
                        dimOk++;
                    }
                    else
                    {
                        dimMismatch++;
                        if (failures.Count < 25)
                        {
                            failures.Add($"{Path.GetFileName(path)} p{image.PageIndex}: SIZ {img.Siz.Width}x{img.Siz.Height} vs declared {image.DeclaredWidth}x{image.DeclaredHeight}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    if (failures.Count < 25)
                    {
                        failures.Add($"{Path.GetFileName(path)} p{image.PageIndex}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }

        _output.WriteLine($"JPX images: {jpx} (single-filter parsed: {parsed})");
        _output.WriteLine($"  SIZ dims ok: {dimOk}, mismatch: {dimMismatch}, parse errors: {errors}");
        _output.WriteLine($"  wavelet: 5/3 reversible={rev}, 9/7 irreversible={irrev}");
        _output.WriteLine($"  decomposition levels: {string.Join(", ", Format(levelsHist))}");
        _output.WriteLine($"  components: {string.Join(", ", Format(compHist))}");
        _output.WriteLine($"  progression: {string.Join(", ", Format(progHist))}");
        foreach (string f in failures)
        {
            _output.WriteLine("  ! " + f);
        }

        Assert.True(errors == 0 && dimMismatch == 0, $"JPX parse issues — mismatch={dimMismatch}, errors={errors}\n" + string.Join("\n", failures));
    }

    private static void Bump(SortedDictionary<int, int> hist, int key)
    {
        hist.TryGetValue(key, out int c);
        hist[key] = c + 1;
    }

    private static IEnumerable<string> Format(SortedDictionary<int, int> hist)
    {
        foreach (KeyValuePair<int, int> kv in hist)
        {
            yield return $"{kv.Key}:{kv.Value}";
        }
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
