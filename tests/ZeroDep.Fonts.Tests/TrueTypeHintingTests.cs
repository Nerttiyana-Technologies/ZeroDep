using System;
using System.Globalization;
using System.IO;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using ZeroDep.Fonts;

namespace ZeroDep.Fonts.Tests;

/// <summary>
/// F6 validation: runs the TrueType bytecode hinter over the <c>private/font-ref/ttf/</c> fixtures at a fixed
/// ppem and dumps the hinted point coordinates (26.6) per glyph to <c>private/font-ref/ours-hint/</c> for the
/// sandbox to diff against FreeType's classic (v35) bytecode interpreter. No-op when fixtures are absent.
/// </summary>
public sealed class TrueTypeHintingTests
{
    private const int Ppem = 48;
    private const int MaxGlyphsPerFont = 80;

    private readonly ITestOutputHelper _output;

    public TrueTypeHintingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void HintsAndDumpsPoints()
    {
        string? dir = FindDir("private", "font-ref", "ttf");
        if (dir is null)
        {
            return;
        }

        string outDir = Path.Combine(Path.GetDirectoryName(dir)!, "ours-hint");
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

            if (!font.HasHinting)
            {
                continue;
            }

            var sb = new StringBuilder();
            sb.Append("ppem ").Append(Ppem).Append(" upem ").Append(font.UnitsPerEm).Append('\n');

            int dumped = 0;
            for (int gid = 0; gid < font.GlyphCount && dumped < MaxGlyphsPerFont; gid++)
            {
                if (!font.TryGetHintedPoints(gid, Ppem, out int[] x, out int[] y, out var raw) || raw is null)
                {
                    continue;
                }

                dumped++;
                sb.Append("G ").Append(gid.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(x.Length).Append('\n');
                for (int i = 0; i < x.Length; i++)
                {
                    sb.Append(x[i]).Append(' ').Append(y[i]).Append(' ').Append(raw.OnCurve[i] ? 1 : 0).Append('\n');
                }
            }

            File.WriteAllText(Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".txt"), sb.ToString());
            fonts++;
        }

        _output.WriteLine($"hinted + dumped {fonts} TrueType fonts at {Ppem}ppem");
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
