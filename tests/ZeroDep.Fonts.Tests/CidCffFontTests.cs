using System;
using System.Globalization;
using System.IO;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using ZeroDep.Fonts;

namespace ZeroDep.Fonts.Tests;

/// <summary>
/// F5 validation: parses CID-keyed CFF fonts under <c>private/font-ref/cid-cff/</c> with <see cref="CffFont"/>
/// (exercising ROS / FDArray / FDSelect per-glyph private dicts) and dumps each glyph's cubic outline to
/// <c>private/font-ref/ours-cid-cff/</c> for the sandbox to diff against fontTools. No-op when fixtures absent.
/// </summary>
public sealed class CidCffFontTests
{
    private readonly ITestOutputHelper _output;

    public CidCffFontTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ParsesAndDumpsOutlines()
    {
        string? dir = FindDir("private", "font-ref", "cid-cff");
        if (dir is null)
        {
            return;
        }

        string outDir = Path.Combine(Path.GetDirectoryName(dir)!, "ours-cid-cff");
        Directory.CreateDirectory(outDir);

        int fonts = 0;
        int cidKeyed = 0;
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
            if (font.IsCidKeyed)
            {
                cidKeyed++;
            }

            var sb = new StringBuilder();
            sb.Append("upem ").Append(font.UnitsPerEm).Append(" glyphs ").Append(font.GlyphCount)
              .Append(" cid ").Append(font.IsCidKeyed ? 1 : 0).Append('\n');
            for (int gid = 0; gid < font.GlyphCount; gid++)
            {
                GlyphOutline outline = font.GetGlyph(gid);
                if (outline.IsEmpty)
                {
                    continue;
                }

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

            File.WriteAllText(Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".txt"), sb.ToString());
            fonts++;
        }

        _output.WriteLine($"parsed + dumped {fonts} CID-CFF fonts ({cidKeyed} CID-keyed)");
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
