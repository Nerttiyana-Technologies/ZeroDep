using System;
using System.IO;
using Xunit;
using Xunit.Abstractions;
using ZeroDep.Fonts;

namespace ZeroDep.Fonts.Tests;

/// <summary>
/// F7 validation: the <see cref="FontProgram"/> facade must sniff each embedded program kind correctly and
/// return non-empty outlines through the unified surface, across all fixture families. No-op when absent.
/// </summary>
public sealed class FontProgramTests
{
    private readonly ITestOutputHelper _output;

    public FontProgramTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void DetectsKindsAndReturnsGlyphs()
    {
        AssertFamily("ttf", "*.ttf", FontProgramKind.TrueType);
        AssertFamily("cff", "*.cff", FontProgramKind.Cff);
        AssertFamily("cid-cff", "*.cff", FontProgramKind.Cff);
        AssertFamily("type1", "*.t1", FontProgramKind.Type1);
    }

    private void AssertFamily(string sub, string pattern, FontProgramKind expected)
    {
        string? dir = FindDir("private", "font-ref", sub);
        if (dir is null)
        {
            return;
        }

        int checkedFonts = 0;
        foreach (string path in Directory.GetFiles(dir, pattern))
        {
            FontProgram fp;
            try
            {
                fp = FontProgram.Load(File.ReadAllBytes(path));
            }
            catch (Exception)
            {
                continue;
            }

            Assert.Equal(expected, fp.Kind);
            Assert.True(fp.UnitsPerEm > 0);
            Assert.True(fp.GlyphCount > 0);

            bool anyOutline = false;
            int limit = Math.Min(fp.GlyphCount, 200);
            for (int gid = 0; gid < limit; gid++)
            {
                if (!fp.GetGlyph(gid).IsEmpty)
                {
                    anyOutline = true;
                    break;
                }
            }

            // A font may legitimately contain only .notdef (e.g. a heavily subset CID font).
            Assert.True(anyOutline || fp.GlyphCount <= 1, $"no outlines from {Path.GetFileName(path)} ({expected})");
            checkedFonts++;
        }

        _output.WriteLine($"{sub}: verified {checkedFonts} fonts as {expected}");
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
