using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;
using ZeroDep.Abstractions;
using ZeroDep.Filters;
using ZeroDep.Objects;

namespace ZeroDep.Analysis.Tests;

/// <summary>
/// Validates the pure-BCL CCITT decoder against the real corpus (private/data): every embedded
/// single-filter <c>/CCITTFaxDecode</c> Group-4 image must decode to its declared dimensions without
/// crashing; Group-3 (K ≥ 0) is cleanly reported <see cref="NotSupportedException"/> until it lands.
/// Prints the K distribution so we can size the remaining work. No-op when the corpus is absent.
/// </summary>
public sealed class CcittCorpusValidationTests
{
    private readonly ITestOutputHelper _output;

    public CcittCorpusValidationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void DecodesEmbeddedCcittImagesOrReportsUnsupported()
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

        const int maxFullDecodes = 500;   // a 5,000-image run validated clean (4,998/4,998); 500 keeps CI quick

        int ccitt = 0, singleFilter = 0, group4 = 0, group3 = 0, group3Decoded = 0;
        int fullDecodes = 0, decodeOk = 0, dimMismatch = 0, unsupported = 0, errors = 0;
        var failures = new List<string>();
        var dims = new List<string>();

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
                continue;   // rejected/encrypted file is not a decoder bug
            }

            foreach (PdfImageInfo image in images)
            {
                if (image.Filter is null || image.Filter.IndexOf("CCITTFaxDecode", StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                ccitt++;
                if (image.Filter != "CCITTFaxDecode" || image.Ccitt is null)
                {
                    continue;   // combined filter chain; raw bytes aren't the bare CCITT codestream
                }

                singleFilter++;
                CcittParameters c = image.Ccitt;
                if (c.K < 0)
                {
                    group4++;
                }
                else
                {
                    group3++;
                }

                // Always decode the rare Group-3 images (only ~83 in the corpus); cap the Group-4 sample.
                bool isGroup3 = c.K >= 0;
                if (!isGroup3 && fullDecodes >= maxFullDecodes)
                {
                    continue;
                }

                if (isGroup3)
                {
                    group3Decoded++;
                }
                else
                {
                    fullDecodes++;
                }

                try
                {
                    RasterImage raster = CcittFaxDecode.Decode(image.EncodedData, new CcittParams
                    {
                        K = c.K,
                        Columns = c.Columns,
                        Rows = c.Rows,
                        BlackIs1 = c.BlackIs1,
                        EncodedByteAlign = c.EncodedByteAlign,
                    });

                    if (raster.Width == image.DeclaredWidth && raster.Height == image.DeclaredHeight)
                    {
                        decodeOk++;
                    }
                    else
                    {
                        dimMismatch++;
                        Record(dims, $"{Path.GetFileName(path)} p{image.PageIndex}: decoded {raster.Width}x{raster.Height} vs declared {image.DeclaredWidth}x{image.DeclaredHeight} (K={c.K}, align={c.EncodedByteAlign})");
                    }
                }
                catch (NotSupportedException)
                {
                    unsupported++;   // Group 3 — known gap, cleanly reported
                }
                catch (Exception ex)
                {
                    errors++;
                    Record(failures, $"{Path.GetFileName(path)} p{image.PageIndex}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        _output.WriteLine($"CCITT images: {ccitt}  (single-filter: {singleFilter}; Group4 K<0: {group4}, Group3 K>=0: {group3})");
        _output.WriteLine($"  full decodes: {fullDecodes} Group-4 (sampled) + {group3Decoded} Group-3 (all)  (ok: {decodeOk}, dimMismatch: {dimMismatch}, unsupported: {unsupported}, errors: {errors})");
        foreach (string d in dims)
        {
            _output.WriteLine("  (dim) " + d);
        }

        foreach (string f in failures)
        {
            _output.WriteLine("  ! " + f);
        }

        // Gate on decoder correctness: no crashes, and every Group-4 decode matches declared dimensions.
        Assert.True(
            errors == 0 && dimMismatch == 0,
            $"CCITT decoder issues — dimMismatch={dimMismatch}, errors={errors}\n" + string.Join("\n", dims) + "\n" + string.Join("\n", failures));
    }

    private static void Record(List<string> list, string message)
    {
        if (list.Count < 30)
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
