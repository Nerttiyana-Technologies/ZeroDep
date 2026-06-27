using System.Collections.Generic;
using Xunit;
using ZeroDep.Abstractions;
using ZeroDep.Analysis;

namespace ZeroDep.Analysis.Tests;

/// <summary>
/// ADR-0008 (Z-G8) regression guard for inter-word spacing in <see cref="TextAnalyzer.BuildPlainText"/>:
/// a positional inter-word gap (no space glyph) of about one space width must produce a space, while
/// flush intra-word glyphs must not be split. Deterministic and self-contained (no corpus / pdftotext).
/// </summary>
public sealed class TextSpacingTests
{
    private static TextRunInfo Run(string text, double x, double width, double fontSize = 20, double spaceEm = 0.25)
        => new TextRunInfo { PageIndex = 0, Y = 100, Text = text, X = x, Width = width, FontSize = fontSize, SpaceWidthEm = spaceEm };

    [Fact]
    public void PositionalGap_OfOneSpaceWidth_InsertsSpace()
    {
        // "proof" ends at x=50; "GLOBUS" starts at x=55 → gap 5 ≈ one space (0.25em × 20 = 5).
        var runs = new List<TextRunInfo> { Run("proof", 0, 50), Run("GLOBUS", 55, 60) };
        Assert.Contains("proof GLOBUS", TextAnalyzer.BuildPlainText(runs));
    }

    [Fact]
    public void FlushGlyphs_AreNotSplit()
    {
        // Adjacent within a word (no gap): "be" ends at 20, "low" starts at 20.
        var runs = new List<TextRunInfo> { Run("be", 0, 20), Run("low", 20, 30) };
        string text = TextAnalyzer.BuildPlainText(runs);
        Assert.Contains("below", text);
        Assert.DoesNotContain("be low", text);
    }

    [Fact]
    public void NarrowSpaceWidth_BelowQuarterEm_StillDetected()
    {
        // A condensed font whose space is 0.18em — below the old 0.25×FontSize threshold but a real space.
        // gap 3.6 (= 0.18em × 20) > 0.5 × 0.18 × 20 = 1.8.
        var runs = new List<TextRunInfo> { Run("a", 0, 10, spaceEm: 0.18), Run("b", 13.6, 10, spaceEm: 0.18) };
        Assert.Contains("a b", TextAnalyzer.BuildPlainText(runs));
    }

    [Fact]
    public void ExistingSpaceGlyph_DoesNotDoubleSpace()
    {
        // A decoded space glyph run between words must not be augmented with a gap-space.
        var runs = new List<TextRunInfo> { Run("a", 0, 10), Run(" ", 10, 5), Run("b", 15, 10) };
        string text = TextAnalyzer.BuildPlainText(runs);
        Assert.Contains("a b", text);
        Assert.DoesNotContain("a  b", text);
    }
}
