using System;
using System.Globalization;
using System.IO;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using ZeroDep.Fonts;
using ZeroDep.Raster;

namespace ZeroDep.Raster.Tests;

/// <summary>
/// F3 validation: renders glyphs from the TrueType fixtures under <c>private/font-ref/ttf/</c> with
/// <see cref="GlyphRasterizer"/> at a fixed pixel size, asserts basic invariants, and dumps each coverage
/// bitmap to <c>private/font-ref/ours-raster/</c> for the sandbox to diff (RMSE) against FreeType's unhinted
/// 256-level renderer. No-op when the fixtures are absent.
/// </summary>
public sealed class GlyphRasterizerTests
{
    private const int PixelSize = 48;
    private const int MaxGlyphsPerFont = 60;

    private readonly ITestOutputHelper _output;

    public GlyphRasterizerTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void DegenerateInputsAreEmpty()
    {
        Assert.Same(GlyphBitmap.Empty, GlyphRasterizer.Render(GlyphOutline.Empty, 0.05));
        Assert.Same(GlyphBitmap.Empty, GlyphRasterizer.Render(GlyphOutline.Empty, 0));
    }

    [Fact]
    public void RendersAndDumpsBitmaps()
    {
        string? dir = FindDir("private", "font-ref", "ttf");
        if (dir is null)
        {
            return;
        }

        string outDir = Path.Combine(Path.GetDirectoryName(dir)!, "ours-raster");
        Directory.CreateDirectory(outDir);

        int fonts = 0;
        foreach (string path in Directory.GetFiles(dir, "*.ttf"))
        {
            TrueTypeFont font;
            try
            {
                font = TrueTypeFont.Load(File.ReadAllBytes(path));
            }
            catch (Exception)
            {
                continue;
            }

            double scale = (double)PixelSize / font.UnitsPerEm;
            var sb = new StringBuilder();
            sb.Append("px ").Append(PixelSize).Append(" upem ").Append(font.UnitsPerEm).Append('\n');

            int dumped = 0;
            for (int gid = 0; gid < font.GlyphCount && dumped < MaxGlyphsPerFont; gid++)
            {
                GlyphOutline outline = font.GetGlyph(gid);
                if (outline.IsEmpty)
                {
                    continue;
                }

                GlyphBitmap bmp = GlyphRasterizer.Render(outline, scale);
                if (bmp.Width <= 0 || bmp.Height <= 0)
                {
                    continue;
                }

                Assert.Equal(bmp.Width * bmp.Height, bmp.Coverage.Length);
                dumped++;

                sb.Append("G ").Append(gid.ToString(CultureInfo.InvariantCulture)).Append(' ')
                  .Append(bmp.Width).Append(' ').Append(bmp.Height).Append(' ')
                  .Append(bmp.Left).Append(' ').Append(bmp.Top).Append('\n');
                for (int y = 0; y < bmp.Height; y++)
                {
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        if (x > 0)
                        {
                            sb.Append(' ');
                        }

                        sb.Append(bmp.Coverage[(y * bmp.Width) + x]);
                    }

                    sb.Append('\n');
                }
            }

            File.WriteAllText(Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".txt"), sb.ToString());
            fonts++;
        }

        _output.WriteLine($"rendered + dumped {fonts} TrueType fonts at {PixelSize}px");
    }

    private static string? FindDir(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string full = dir.FullName;
            foreach (string part in parts)
            {
                full = Path.Combine(full, part);
            }

            if (Directory.Exists(full))
            {
                return full;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
