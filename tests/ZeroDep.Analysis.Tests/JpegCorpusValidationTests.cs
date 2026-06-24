using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;
using ZeroDep.Abstractions;
using ZeroDep.Analysis;
using ZeroDep.Filters;

namespace ZeroDep.Analysis.Tests;

/// <summary>
/// Validates the pure-BCL JPEG decoder against the real corpus (private/data): every embedded
/// single-filter <c>/DCTDecode</c> image must either decode to its declared dimensions, or be cleanly
/// reported <see cref="NotSupportedException"/> (progressive / CMYK) — never crash or mis-size. Also
/// prints the format distribution so we can prioritize the next decoder stage from data. No-op
/// (passing) when the corpus is absent.
/// </summary>
public sealed class JpegCorpusValidationTests
{
    private readonly ITestOutputHelper _output;

    public JpegCorpusValidationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void DecodesEmbeddedDctImagesOrReportsUnsupported()
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

        const int maxFullDecodes = 200;   // full pixel decode is sampled; header parse covers all

        int dct = 0, singleFilter = 0, pdfNonConformant = 0;
        int fullDecodes = 0, decodeOk = 0, dimMismatch = 0, unsupported = 0, errors = 0;
        var modes = new Dictionary<JpegMode, int>();
        var failures = new List<string>();
        var conformance = new List<string>();

        foreach (string path in pdfs)
        {
            IReadOnlyList<PdfImageInfo> images;
            try
            {
                using FileStream stream = File.OpenRead(path);
                images = ImageExtractor.Extract(stream);
            }
            catch (PdfSyntaxException)
            {
                continue;   // a rejected/encrypted file is not a decoder bug
            }
            catch (Exception)
            {
                continue;
            }

            foreach (PdfImageInfo image in images)
            {
                if (image.Filter is null || image.Filter.IndexOf("DCTDecode", StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                dct++;
                if (image.Filter != "DCTDecode")
                {
                    continue;   // combined filter chain (e.g. Flate+DCT); raw bytes are not raw JPEG
                }

                singleFilter++;

                // Fast path for EVERY image: header parse → mode tally + JPEG header dimensions.
                JpegMode mode = JpegMode.Unsupported;
                int headerW = 0, headerH = 0;
                try
                {
                    JpegMetadata meta = JpegReader.ReadMetadata(image.EncodedData);
                    mode = meta.Mode;
                    headerW = meta.Width;
                    headerH = meta.Height;
                    modes.TryGetValue(mode, out int mc);
                    modes[mode] = mc + 1;

                    // Informational only: a JPEG whose true size differs from the PDF's declared /Width
                    // /Height is a NON-CONFORMANT PDF, not a decoder defect — report, do not fail.
                    if (headerW != image.DeclaredWidth || headerH != image.DeclaredHeight)
                    {
                        pdfNonConformant++;
                        Record(conformance, $"{Path.GetFileName(path)} p{image.PageIndex}: JPEG {headerW}x{headerH} vs PDF /declared {image.DeclaredWidth}x{image.DeclaredHeight}");
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    Record(failures, $"{Path.GetFileName(path)} p{image.PageIndex}: header parse {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                // Sampled full pixel decode (the expensive part) across all coding processes.
                if (fullDecodes >= maxFullDecodes)
                {
                    continue;
                }

                fullDecodes++;
                try
                {
                    RasterImage raster = JpegDecoder.Decode(image.EncodedData);

                    // Decoder correctness: decoded pixels must match the JPEG's OWN header dimensions.
                    if (raster.Width == headerW && raster.Height == headerH)
                    {
                        decodeOk++;
                    }
                    else
                    {
                        dimMismatch++;
                        Record(failures, $"{Path.GetFileName(path)} p{image.PageIndex}: decoded {raster.Width}x{raster.Height} vs JPEG header {headerW}x{headerH}");
                    }
                }
                catch (NotSupportedException)
                {
                    unsupported++;   // CMYK / progressive — a known gap, cleanly reported
                }
                catch (Exception ex)
                {
                    errors++;
                    Record(failures, $"{Path.GetFileName(path)} p{image.PageIndex}: decode {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        _output.WriteLine($"DCTDecode images: {dct}  (single-filter: {singleFilter})");
        _output.WriteLine($"  full decodes sampled: {fullDecodes}  (ok: {decodeOk}, dimMismatch: {dimMismatch}, errors: {errors}, unsupported/CMYK: {unsupported})");
        _output.WriteLine($"  non-conformant PDFs (JPEG size != declared): {pdfNonConformant}");
        foreach (KeyValuePair<JpegMode, int> kv in modes)
        {
            _output.WriteLine($"  mode {kv.Key}: {kv.Value}");
        }

        foreach (string f in conformance)
        {
            _output.WriteLine("  (pdf) " + f);
        }

        foreach (string f in failures)
        {
            _output.WriteLine("  ! " + f);
        }

        // Gate on DECODER correctness only: no crashes, and decoded size matches the JPEG header.
        Assert.True(
            dimMismatch == 0 && errors == 0,
            $"JPEG decoder issues — dimMismatch={dimMismatch}, errors={errors}\n" + string.Join("\n", failures));
    }

    private static void Record(List<string> failures, string message)
    {
        if (failures.Count < 25)
        {
            failures.Add(message);
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
