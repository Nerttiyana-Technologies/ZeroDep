using System;
using System.Globalization;
using System.IO;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using ZeroDep.Fonts;

namespace ZeroDep.Fonts.Tests;

/// <summary>
/// F1 validation: parses the embedded TrueType fonts under <c>private/font-ref/ttf/</c>, asserts basic
/// invariants, and dumps each glyph outline (font units) to <c>private/font-ref/ours/</c> so the sandbox can
/// diff control points against FreeType (<c>FT_LOAD_NO_SCALE</c>). No-op when the font fixtures are absent.
/// </summary>
public sealed class TrueTypeFontTests
{
    private readonly ITestOutputHelper _output;

    public TrueTypeFontTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ParsesAndDumpsOutlines()
    {
        string? ttfDir = FindDir("private", "font-ref", "ttf");
        if (ttfDir is null)
        {
            return;
        }

        string outDir = Path.Combine(Path.GetDirectoryName(ttfDir)!, "ours");
        Directory.CreateDirectory(outDir);

        int fonts = 0;
        foreach (string path in Directory.GetFiles(ttfDir, "*.ttf"))
        {
            byte[] data = File.ReadAllBytes(path);
            TrueTypeFont font;
            try
            {
                font = TrueTypeFont.Load(data);
            }
            catch (Exception)
            {
                continue;
            }

            Assert.True(font.UnitsPerEm > 0);
            Assert.True(font.GlyphCount > 0);

            var sb = new StringBuilder();
            sb.Append("upem ").Append(font.UnitsPerEm).Append(" glyphs ").Append(font.GlyphCount).Append('\n');

            int dumped = 0;
            bool anyNonEmpty = false;
            for (int gid = 0; gid < font.GlyphCount && dumped < 400; gid++)
            {
                GlyphOutline outline = font.GetGlyph(gid);
                if (outline.IsEmpty)
                {
                    continue;
                }

                anyNonEmpty = true;
                dumped++;
                sb.Append("G ").Append(gid.ToString(CultureInfo.InvariantCulture)).Append('\n');
                foreach (GlyphContour c in outline.Contours)
                {
                    sb.Append("C ").Append(N(c.StartX)).Append(' ').Append(N(c.StartY)).Append('\n');
                    foreach (GlyphSegment s in c.Segments)
                    {
                        switch (s.Type)
                        {
                            case SegmentType.Line:
                                sb.Append("L ").Append(N(s.EndX)).Append(' ').Append(N(s.EndY)).Append('\n');
                                break;
                            case SegmentType.Quadratic:
                                sb.Append("Q ").Append(N(s.Control1X)).Append(' ').Append(N(s.Control1Y)).Append(' ')
                                  .Append(N(s.EndX)).Append(' ').Append(N(s.EndY)).Append('\n');
                                break;
                            default:
                                sb.Append("B ").Append(N(s.Control1X)).Append(' ').Append(N(s.Control1Y)).Append(' ')
                                  .Append(N(s.Control2X)).Append(' ').Append(N(s.Control2Y)).Append(' ')
                                  .Append(N(s.EndX)).Append(' ').Append(N(s.EndY)).Append('\n');
                                break;
                        }
                    }
                }
            }

            Assert.True(anyNonEmpty, $"no non-empty glyphs parsed from {Path.GetFileName(path)}");
            File.WriteAllText(Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".txt"), sb.ToString());
            fonts++;
        }

        _output.WriteLine($"parsed + dumped {fonts} fonts");
    }

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

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
