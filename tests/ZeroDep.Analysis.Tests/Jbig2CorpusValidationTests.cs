using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;
using ZeroDep.Abstractions;
using ZeroDep.Filters.Jbig2;

namespace ZeroDep.Analysis.Tests;

/// <summary>
/// Validates the JBIG2 generic-region decoder against the real corpus: parse every embedded
/// single-filter <c>/JBIG2Decode</c> image's segments (cheap, all images) and full-decode a sample,
/// asserting no crashes. Prints the segment-type distribution so we can size the symbol/text stage.
/// No-op when the corpus is absent.
/// </summary>
public sealed class Jbig2CorpusValidationTests
{
    private readonly ITestOutputHelper _output;

    public Jbig2CorpusValidationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ParsesAndDecodesJbig2WithoutCrashing()
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

        const int maxFullDecodes = 500;   // coverage (Inspect) runs on ALL images; this samples full decode

        int jbig2 = 0, singleFilter = 0, genericOnly = 0, hasText = 0, hasHalftone = 0;
        int fullDecodes = 0, decodeOk = 0, errors = 0;
        int supported = 0, usesHuffman = 0, usesRefinement = 0, usesHalftoneCap = 0;
        var typeHistogram = new SortedDictionary<int, int>();
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
                if (image.Filter is null || image.Filter.IndexOf("JBIG2Decode", StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                jbig2++;
                if (image.Filter != "JBIG2Decode")
                {
                    continue;
                }

                singleFilter++;

                Jbig2Capabilities caps = Jbig2Decode.Inspect(image.EncodedData, image.Jbig2Globals);
                if (caps.Supported)
                {
                    supported++;
                }

                if (caps.UsesHuffman)
                {
                    usesHuffman++;
                }

                if (caps.UsesRefinement)
                {
                    usesRefinement++;
                }

                if (caps.UsesHalftone)
                {
                    usesHalftoneCap++;
                }

                IReadOnlyList<int> types;
                try
                {
                    types = Jbig2Decode.SegmentTypes(image.EncodedData);
                }
                catch (Exception)
                {
                    errors++;
                    continue;
                }

                bool generic = false, text = false, halftone = false;
                foreach (int t in types)
                {
                    typeHistogram.TryGetValue(t, out int c);
                    typeHistogram[t] = c + 1;
                    if (t is 36 or 38 or 39)
                    {
                        generic = true;
                    }
                    else if (t is 4 or 6 or 7 or 0)
                    {
                        text = true;
                    }
                    else if (t is 16 or 20 or 22 or 23)
                    {
                        halftone = true;
                    }
                }

                if (text)
                {
                    hasText++;
                }
                else if (halftone)
                {
                    hasHalftone++;
                }
                else if (generic)
                {
                    genericOnly++;
                }

                if (fullDecodes >= maxFullDecodes)
                {
                    continue;
                }

                fullDecodes++;
                try
                {
                    var raster = Jbig2Decode.Decode(image.EncodedData, image.Jbig2Globals, image.DeclaredWidth, image.DeclaredHeight);
                    if (raster.Width == image.DeclaredWidth && raster.Height == image.DeclaredHeight)
                    {
                        decodeOk++;
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    Record(failures, $"{Path.GetFileName(path)} p{image.PageIndex}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        _output.WriteLine($"JBIG2 images: {jbig2}  (single-filter: {singleFilter})");
        _output.WriteLine($"  fully supported: {supported} ({(singleFilter == 0 ? 0 : 100.0 * supported / singleFilter):F1}%)  " +
            $"uses Huffman: {usesHuffman}, refinement: {usesRefinement}, halftone: {usesHalftoneCap}");
        _output.WriteLine($"  generic-only: {genericOnly}, has symbol/text: {hasText}, has halftone: {hasHalftone}");
        _output.WriteLine($"  full decodes sampled: {fullDecodes}  (ok dims: {decodeOk}, errors: {errors})");
        _output.WriteLine("  segment-type histogram (type: count):");
        foreach (KeyValuePair<int, int> kv in typeHistogram)
        {
            _output.WriteLine($"    {kv.Key}: {kv.Value}");
        }

        foreach (string f in failures)
        {
            _output.WriteLine("  ! " + f);
        }

        Assert.True(errors == 0, $"JBIG2 decoder crashed on {errors} image(s)\n" + string.Join("\n", failures));
    }

    private static void Record(List<string> list, string message)
    {
        if (list.Count < 25)
        {
            list.Add(message);
        }
    }

    private static string? FindDataDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (string candidate in new[] { Path.Combine("private", "data"), "data" })
            {
                string full = Path.Combine(dir.FullName, candidate);
                if (Directory.Exists(full))
                {
                    return full;
                }
            }

            dir = dir.Parent;
        }

        return null;
    }
}
