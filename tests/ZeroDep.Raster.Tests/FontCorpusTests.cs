using System;
using System.IO;
using Xunit;
using Xunit.Abstractions;
using ZeroDep.Fonts;
using ZeroDep.Raster;

namespace ZeroDep.Raster.Tests;

/// <summary>
/// F8 / F-G5 validation: loads every embedded font program under <c>private/font-ref/corpus/</c> through the
/// <see cref="FontProgram"/> facade and renders each glyph unhinted and (where supported) hinted. The gate is
/// zero crashes — malformed programs must be isolated (load fault counted, not thrown), and every produced
/// outline/bitmap must satisfy basic invariants. No-op when fixtures are absent.
/// </summary>
public sealed class FontCorpusTests
{
    private const int MaxGlyphsPerFont = 220;

    private readonly ITestOutputHelper _output;

    public FontCorpusTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ParseAndRenderEntireCorpusWithoutCrashing()
    {
        string? dir = FindDir("private", "font-ref", "corpus");
        if (dir is null)
        {
            return;
        }

        int loaded = 0;
        int loadFaults = 0;
        long glyphs = 0;
        long nonEmpty = 0;

        foreach (string path in Directory.GetFiles(dir, "*.bin"))
        {
            byte[] data;
            try
            {
                data = File.ReadAllBytes(path);
            }
            catch (IOException)
            {
                continue;
            }

            FontProgram font;
            try
            {
                font = FontProgram.Load(data);
            }
            catch (Exception)
            {
                loadFaults++; // malformed program isolated, not a crash
                continue;
            }

            loaded++;
            int count = Math.Min(font.GlyphCount, MaxGlyphsPerFont);
            for (int gid = 0; gid < count; gid++)
            {
                GlyphOutline outline = font.GetGlyph(gid);
                glyphs++;
                if (!outline.IsEmpty)
                {
                    nonEmpty++;
                }

                GlyphBitmap unhinted = GlyphRenderer.Render(font, gid, 24, hinted: false);
                Assert.Equal(unhinted.Width * unhinted.Height, unhinted.Coverage.Length);

                GlyphBitmap hinted = GlyphRenderer.Render(font, gid, 24, hinted: true);
                Assert.Equal(hinted.Width * hinted.Height, hinted.Coverage.Length);
            }
        }

        _output.WriteLine($"corpus: loaded {loaded}, isolated {loadFaults} faults, {glyphs} glyphs ({nonEmpty} non-empty)");
        Assert.True(loaded > 0, "no corpus fonts loaded");
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
