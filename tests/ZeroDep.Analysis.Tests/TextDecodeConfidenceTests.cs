using System;
using Xunit;
using ZeroDep.Analysis;

namespace ZeroDep.Analysis.Tests;

/// <summary>
/// T3 — the per-page text-decode trust score (ADR-0007 §2.1) via <see cref="PageClassifier.DecodeConfidence"/>:
/// authoritative glyphs earn full credit, blind-guess fallbacks earn partial, unmapped earn none, and a
/// text-free page reports 1.0. Deterministic (fixed rounding).
/// </summary>
public sealed class TextDecodeConfidenceTests
{
    [Fact]
    public void AllAuthoritative_IsOne()
        => Assert.Equal(1.0, PageClassifier.DecodeConfidence(auth: 100, fallback: 0, unmapped: 0));

    [Fact]
    public void TextFreePage_ReportsOne()
        => Assert.Equal(1.0, PageClassifier.DecodeConfidence(auth: 0, fallback: 0, unmapped: 0));

    [Fact]
    public void AllUnmapped_IsZero()
        => Assert.Equal(0.0, PageClassifier.DecodeConfidence(auth: 0, fallback: 0, unmapped: 50));

    [Fact]
    public void AllFallback_EarnsPartialCredit()
    {
        // Blind-guess pages should land low (below a typical consumer threshold) but not zero.
        double score = PageClassifier.DecodeConfidence(auth: 0, fallback: 80, unmapped: 0);
        Assert.True(score > 0 && score < 0.5, $"expected low partial credit, got {score}");
    }

    [Fact]
    public void Monotonic_MoreAuthoritativeRaisesScore()
    {
        double low = PageClassifier.DecodeConfidence(auth: 10, fallback: 90, unmapped: 0);
        double high = PageClassifier.DecodeConfidence(auth: 90, fallback: 10, unmapped: 0);
        Assert.True(high > low);
    }

    [Fact]
    public void Deterministic_SameInputsSameOutput()
    {
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(
                PageClassifier.DecodeConfidence(37, 11, 5),
                PageClassifier.DecodeConfidence(37, 11, 5));
        }
    }

    [Fact]
    public void Bounded_StaysInUnitInterval()
    {
        double score = PageClassifier.DecodeConfidence(auth: 1, fallback: 1, unmapped: 1);
        Assert.InRange(score, 0.0, 1.0);
    }
}
