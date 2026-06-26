using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;
using ZeroDep.Analysis;
using ZeroDep.Filters;

namespace ZeroDep.Analysis.Tests;

/// <summary>
/// C-G1 harness (ADR-0004 §4): decodes colour-normalized images from the corpus via
/// <see cref="ColorImageDecoder"/> and writes them as BMPs + a <c>manifest.txt</c> under
/// <c>private/color-ref/</c>, so the sandbox can pixel-diff against poppler <c>pdfimages</c> (which also
/// applies the PDF colour space). A per-family cap keeps coverage broad and the run fast. Manifest line:
/// <c>idx|relPdf|page|family|w|h</c>. No-op when the corpus is absent.
/// </summary>
public sealed class ColorDumpSamplesTests
{
    private readonly ITestOutputHelper _output;

    public ColorDumpSamplesTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void DumpForReferenceDiff()
    {
        string? dataDir = FindDataDirectory();
        if (dataDir is null)
        {
            return;
        }

        string outDir = Path.Combine(dataDir, "..", "color-ref");
        Directory.CreateDirectory(outDir);

        const int perFamilyCap = 10;
        const int totalCap = 70;
        const int maxPdfsScanned = 600;
        var familyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var manifest = new List<string>();
        int idx = 0;
        int pdfsScanned = 0;

        bool Want(string? family)
        {
            familyCounts.TryGetValue(family ?? "Unknown", out int c);
            return c < perFamilyCap;
        }

        foreach (string path in Directory.EnumerateFiles(dataDir, "*.pdf", SearchOption.AllDirectories))
        {
            if (idx >= totalCap || pdfsScanned >= maxPdfsScanned)
            {
                break;
            }

            pdfsScanned++;

            IReadOnlyList<ColorImage> images;
            try
            {
                using FileStream stream = File.OpenRead(path);
                images = ColorImageDecoder.Extract(stream, null, maxImages: 8, shouldDecode: Want);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (ColorImage image in images)
            {
                string family = image.ColorSpaceFamily ?? "Unknown";
                familyCounts.TryGetValue(family, out int count);
                if (count >= perFamilyCap)
                {
                    continue;
                }

                RasterImage raster = image.Image;
                int w = raster.Width, h = raster.Height;
                if (w is < 32 or > 3000 || h is < 32 or > 3500 || raster.Samples.Length == 0)
                {
                    continue;
                }

                try
                {
                    File.WriteAllBytes(Path.Combine(outDir, $"img{idx}.bmp"), Bmp(raster));
                }
                catch (Exception)
                {
                    continue;
                }

                string rel = Path.GetRelativePath(dataDir, path).Replace('\\', '/');
                manifest.Add($"{idx}|{rel}|{image.PageIndex}|{family}|{w}|{h}");
                familyCounts[family] = count + 1;
                idx++;
                if (idx >= totalCap)
                {
                    break;
                }
            }
        }

        File.WriteAllLines(Path.Combine(outDir, "manifest.txt"), manifest);
        _output.WriteLine($"dumped {idx} colour images into {outDir}");
        foreach (KeyValuePair<string, int> kv in familyCounts)
        {
            _output.WriteLine($"  {kv.Key}: {kv.Value}");
        }
    }

    private static byte[] Bmp(RasterImage img)
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
                byte r;
                byte g;
                byte b;
                if (gray)
                {
                    r = g = b = si < img.Samples.Length ? img.Samples[si] : (byte)0;
                }
                else
                {
                    r = si < img.Samples.Length ? img.Samples[si] : (byte)0;
                    g = si + 1 < img.Samples.Length ? img.Samples[si + 1] : (byte)0;
                    b = si + 2 < img.Samples.Length ? img.Samples[si + 2] : (byte)0;
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
