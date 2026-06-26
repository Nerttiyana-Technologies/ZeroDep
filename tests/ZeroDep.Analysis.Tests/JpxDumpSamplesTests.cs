using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;
using ZeroDep.Abstractions;
using ZeroDep.Filters;
using ZeroDep.Filters.Jpx;

namespace ZeroDep.Analysis.Tests;

/// <summary>
/// Diagnostic (not a gate): decodes JPEG 2000 (<c>/JPXDecode</c>) corpus images and writes them as BMPs
/// plus a <c>manifest.txt</c> under <c>private/jpx-ref/</c>, so the sandbox can pixel-diff against poppler
/// <c>pdfimages</c> (the same reference-diff harness used for JBIG2). Reversible (5/3) images are dumped
/// first for the bit-exact gate; a few irreversible (9/7) images follow for tolerance checking. The
/// manifest line format is <c>idx|relPdfPath|width|height|wavelet</c>. No-op when the corpus is absent.
/// </summary>
public sealed class JpxDumpSamplesTests
{
    private readonly ITestOutputHelper _output;

    public JpxDumpSamplesTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void DumpForReferenceDiff()
    {
        string? dataDir = FindDataDirectory();
        if (dataDir is null)
        {
            return;
        }

        string outDir = Path.Combine(dataDir, "..", "jpx-ref");
        Directory.CreateDirectory(outDir);

        const int wantReversible = 40;
        const int wantIrreversible = 20;
        int rev = 0;
        int irr = 0;
        var manifest = new List<string>();

        foreach (string path in Directory.EnumerateFiles(dataDir, "*.pdf", SearchOption.AllDirectories))
        {
            if (rev >= wantReversible && irr >= wantIrreversible)
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
                if (image.Filter != "JPXDecode")
                {
                    continue;
                }

                int w = image.DeclaredWidth, h = image.DeclaredHeight;
                if (w is < 64 or > 3000 || h is < 64 or > 3500)
                {
                    continue;
                }

                // Unambiguous within the PDF so pdfimages output can be matched by dimensions.
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
                    continue;
                }

                bool reversible;
                try
                {
                    reversible = JpxCodestream.Parse(image.EncodedData).Cod.Reversible;
                }
                catch (Exception)
                {
                    continue;
                }

                if (reversible ? rev >= wantReversible : irr >= wantIrreversible)
                {
                    continue;
                }

                try
                {
                    RasterImage raster = JpxDecode.Decode(image.EncodedData, w, h);
                    int idx = reversible ? rev : (1000 + irr);
                    string prefix = reversible ? "rev" : "irr";
                    File.WriteAllBytes(Path.Combine(outDir, $"{prefix}{(reversible ? rev : irr)}.bmp"), Bmp(raster));
                    string rel = Path.GetRelativePath(dataDir, path).Replace('\\', '/');
                    manifest.Add($"{idx}|{rel}|{w}|{h}|{(reversible ? "5/3" : "9/7")}");
                    if (reversible)
                    {
                        rev++;
                    }
                    else
                    {
                        irr++;
                    }
                }
                catch (Exception)
                {
                    // skip undecodable
                }
            }
        }

        File.WriteAllLines(Path.Combine(outDir, "manifest.txt"), manifest);
        _output.WriteLine($"dumped {rev} reversible + {irr} irreversible JPX decodes into {outDir}");
    }

    // 24-bit BMP from a 1-component grayscale or 3-component RGB raster.
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
