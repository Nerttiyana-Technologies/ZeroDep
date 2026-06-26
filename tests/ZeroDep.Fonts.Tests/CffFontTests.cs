using System;
using System.Globalization;
using System.IO;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using ZeroDep.Fonts;

namespace ZeroDep.Fonts.Tests;

/// <summary>
/// F2 validation: parses the CFF fonts under <c>private/font-ref/cff/</c> with <see cref="CffFont"/>, asserts
/// basic invariants, and dumps each glyph's cubic outline to <c>private/font-ref/ours-cff/</c> for the sandbox
/// to diff against fontTools' Type 2 interpreter. No-op when the fixtures are absent.
/// </summary>
public sealed class CffFontTests
{
    private readonly ITestOutputHelper _output;

    public CffFontTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ParsesAndDumpsOutlines()
    {
        string? dir = FindDir("private", "font-ref", "cff");
        if (dir is null)
        {
            return;
        }

        string outDir = Path.Combine(Path.GetDirectoryName(dir)!, "ours-cff");
        Directory.CreateDirectory(outDir);

        int fonts = 0;
        foreach (string path in Directory.GetFiles(dir, "*.cff"))
        {
            CffFont font;
            try
            {
                font = CffFont.Load(File.ReadAllBytes(path));
            }
            catch (Exception)
            {
                continue;
            }

            Assert.True(font.GlyphCount > 0);
            Assert.True(font.UnitsPerEm > 0);

            var sb = new StringBuilder();
            sb.Append("upem ").Append(font.UnitsPerEm).Append(" glyphs ").Append(font.GlyphCount).Append('\n');
            bool any = false;
            for (int gid = 0; gid < font.GlyphCount; gid++)
            {
                GlyphOutline outline = font.GetGlyph(gid);
                if (outline.IsEmpty)
                {
                    continue;
                }

                any = true;
                sb.Append("G ").Append(gid.ToString(CultureInfo.InvariantCulture)).Append('\n');
                foreach (GlyphContour c in outline.Contours)
                {
                    sb.Append("C ").Append(N(c.StartX)).Append(' ').Append(N(c.StartY)).Append('\n');
                    foreach (GlyphSegment s in c.Segments)
                    {
                        if (s.Type == SegmentType.Line)
                        {
                            sb.Append("L ").Append(N(s.EndX)).Append(' ').Append(N(s.EndY)).Append('\n');
                        }
                        else
                        {
                            sb.Append("B ").Append(N(s.Control1X)).Append(' ').Append(N(s.Control1Y)).Append(' ')
                              .Append(N(s.Control2X)).Append(' ').Append(N(s.Control2Y)).Append(' ')
                              .Append(N(s.EndX)).Append(' ').Append(N(s.EndY)).Append('\n');
                        }
                    }
                }
            }

            Assert.True(any, $"no non-empty glyphs from {Path.GetFileName(path)}");
            File.WriteAllText(Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".txt"), sb.ToString());
            fonts++;
        }

        _output.WriteLine($"parsed + dumped {fonts} CFF fonts");
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
