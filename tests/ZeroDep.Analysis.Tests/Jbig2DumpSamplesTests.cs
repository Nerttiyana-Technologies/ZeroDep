using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;
using ZeroDep.Abstractions;
using ZeroDep.Filters;
using ZeroDep.Filters.Jbig2;

namespace ZeroDep.Analysis.Tests;

/// <summary>
/// Diagnostic (not a gate): decodes the first few generic-only JBIG2 images from the corpus and writes
/// them as BMPs under <c>private/jbig2-samples/</c> for visual inspection of pixel correctness.
/// Stops as soon as it has enough samples, so it is fast. No-op when the corpus is absent.
/// </summary>
public sealed class Jbig2DumpSamplesTests
{
    private readonly ITestOutputHelper _output;

    public Jbig2DumpSamplesTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void DumpGenericSamples()
    {
        string? dataDir = FindDataDirectory();
        if (dataDir is null)
        {
            return;
        }

        string outDir = Path.Combine(dataDir, "..", "jbig2-samples");
        Directory.CreateDirectory(outDir);

        const int want = 4;
        int dumped = 0;

        foreach (string path in Directory.EnumerateFiles(dataDir, "*.pdf", SearchOption.AllDirectories))
        {
            if (dumped >= want)
            {
                break;
            }

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
                if (dumped >= want || image.Filter != "JBIG2Decode")
                {
                    continue;
                }

                // Any text-region image (symbols may live in globals, now provided).
                bool hasTextRegion = false;
                foreach (int t in Jbig2Decode.SegmentTypes(image.EncodedData))
                {
                    if (t is 4 or 6 or 7)
                    {
                        hasTextRegion = true;
                    }
                }

                if (!hasTextRegion)
                {
                    continue;
                }

                if (image.DeclaredWidth is < 16 or > 2000 || image.DeclaredHeight is < 16 or > 2600)
                {
                    continue;
                }

                try
                {
                    RasterImage raster = Jbig2Decode.Decode(image.EncodedData, image.Jbig2Globals, image.DeclaredWidth, image.DeclaredHeight);
                    File.WriteAllBytes(Path.Combine(outDir, $"text{dumped}.bmp"), GrayBmp(raster));
                    dumped++;
                }
                catch (Exception)
                {
                    // skip
                }
            }
        }
    }

    [Fact]
    public void DumpForReferenceDiff()
    {
        string? dataDir = FindDataDirectory();
        if (dataDir is null)
        {
            return;
        }

        string outDir = Path.Combine(dataDir, "..", "jbig2-ref");
        Directory.CreateDirectory(outDir);

        const int want = 60;
        int idx = 0;
        var manifest = new List<string>();

        foreach (string path in Directory.EnumerateFiles(dataDir, "*.pdf", SearchOption.AllDirectories))
        {
            if (idx >= want)
            {
                break;
            }

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

            // Only fully-supported JBIG2 images, of a reasonable size, and unambiguous within the PDF
            // (no other image shares the same dimensions) so pdfimages output can be matched by size.
            foreach (PdfImageInfo image in images)
            {
                if (idx >= want || image.Filter != "JBIG2Decode")
                {
                    continue;
                }

                if (!Jbig2Decode.Inspect(image.EncodedData, image.Jbig2Globals).Supported)
                {
                    continue;
                }

                int w = image.DeclaredWidth, h = image.DeclaredHeight;
                if (w is < 64 or > 3000 || h is < 64 or > 3500)
                {
                    continue;
                }

                int sameDims = 0;
                foreach (PdfImageInfo other in images)
                {
                    if (other.DeclaredWidth == w && other.DeclaredHeight == h)
                    {
                        sameDims++;
                    }
                }

                if (sameDims != 1)
                {
                    continue; // ambiguous to match against pdfimages output
                }

                try
                {
                    RasterImage raster = Jbig2Decode.Decode(image.EncodedData, image.Jbig2Globals, w, h);
                    File.WriteAllBytes(Path.Combine(outDir, $"ours{idx}.bmp"), GrayBmp(raster));
                    string rel = Path.GetRelativePath(dataDir, path).Replace('\\', '/');
                    manifest.Add($"{idx}|{rel}|{w}|{h}");
                    idx++;
                }
                catch (Exception)
                {
                    // skip
                }
            }
        }

        File.WriteAllLines(Path.Combine(outDir, "manifest.txt"), manifest);
        _output.WriteLine($"dumped {idx} JBIG2 decodes + manifest into {outDir}");
    }

    // Minimal 24-bit BMP from a 1-component grayscale raster.
    private static byte[] GrayBmp(RasterImage img)
    {
        int w = img.Width, h = img.Height;
        int rowSize = ((w * 3) + 3) / 4 * 4;
        int dataSize = rowSize * h;
        var bmp = new byte[54 + dataSize];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        WriteI32(bmp, 2, 54 + dataSize);
        WriteI32(bmp, 10, 54);
        WriteI32(bmp, 14, 40);
        WriteI32(bmp, 18, w);
        WriteI32(bmp, 22, h);
        bmp[26] = 1;
        bmp[28] = 24;
        WriteI32(bmp, 34, dataSize);

        bool gray = img.Components == 1;
        for (int y = 0; y < h; y++)
        {
            int dst = 54 + ((h - 1 - y) * rowSize);
            for (int x = 0; x < w; x++)
            {
                int si = ((y * w) + x) * img.Components;
                byte v = si < img.Samples.Length ? img.Samples[si] : (byte)0;
                byte r = v, g = v, b = v;
                if (!gray && si + 2 < img.Samples.Length)
                {
                    r = img.Samples[si];
                    g = img.Samples[si + 1];
                    b = img.Samples[si + 2];
                }

                int di = dst + (x * 3);
                bmp[di] = b;
                bmp[di + 1] = g;
                bmp[di + 2] = r;
            }
        }

        return bmp;
    }

    private static void WriteI32(byte[] buf, int off, int val)
    {
        buf[off] = (byte)val;
        buf[off + 1] = (byte)(val >> 8);
        buf[off + 2] = (byte)(val >> 16);
        buf[off + 3] = (byte)(val >> 24);
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
