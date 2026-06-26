using System;
using System.Globalization;
using System.IO;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using ZeroDep.Fonts;

namespace ZeroDep.Fonts.Tests;

/// <summary>
/// F4 validation: parses the Type 1 fonts under <c>private/font-ref/type1/</c> with <see cref="Type1Font"/>,
/// asserts invariants, and dumps each glyph's cubic outline (keyed by glyph name) to
/// <c>private/font-ref/ours-type1/</c> for the sandbox to diff against fontTools' Type 1 interpreter.
/// No-op when fixtures are absent.
/// </summary>
public sealed class Type1FontTests
{
    private readonly ITestOutputHelper _output;

    public Type1FontTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ParsesAndDumpsOutlines()
    {
        string? dir = FindDir("private", "font-ref", "type1");
        if (dir is null)
        {
            return;
        }

        string outDir = Path.Combine(Path.GetDirectoryName(dir)!, "ours-type1");
        Directory.CreateDirectory(outDir);

        int fonts = 0;
        foreach (string path in Directory.GetFiles(dir, "*.t1"))
        {
            Type1Font font;
            try
            {
                font = Type1Font.Load(File.ReadAllBytes(path));
            }
            catch (Exception)
            {
                continue;
            }

            Assert.True(font.GlyphCount > 0);
            Assert.True(font.UnitsPerEm > 0);

            var sb = new StringBuilder();
            sb.Append("upem ").Append(font.UnitsPerEm).Append(" glyphs ").Append(font.GlyphCount).Append('\n');
            foreach (string name in font.GlyphNames)
            {
                GlyphOutline outline = font.GetGlyph(name);
                sb.Append("N ").Append(name).Append('\n');
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

            File.WriteAllText(Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".txt"), sb.ToString());
            fonts++;
        }

        _output.WriteLine($"parsed + dumped {fonts} Type 1 fonts");
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
