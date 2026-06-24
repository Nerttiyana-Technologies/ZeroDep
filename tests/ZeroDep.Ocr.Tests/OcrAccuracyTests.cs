using Xunit;
using ZeroDep.Ocr;

namespace ZeroDep.Ocr.Tests;

/// <summary>Character/word error-rate metrics used by the OCR accuracy benchmark.</summary>
public sealed class OcrAccuracyTests
{
    [Fact]
    public void Cer_IdenticalText_IsZero()
        => Assert.Equal(0.0, OcrAccuracy.CharacterErrorRate("Invoice 2026", "Invoice 2026"), 6);

    [Fact]
    public void Cer_OneSubstitutionInFiveChars_IsOneFifth()
        => Assert.Equal(0.2, OcrAccuracy.CharacterErrorRate("hello", "hallo"), 6);

    [Fact]
    public void Cer_EmptyReference_NonEmptyHypothesis_IsOne()
        => Assert.Equal(1.0, OcrAccuracy.CharacterErrorRate("", "garbage"), 6);

    [Fact]
    public void Cer_BothEmpty_IsZero()
        => Assert.Equal(0.0, OcrAccuracy.CharacterErrorRate("", ""), 6);

    [Fact]
    public void Wer_OneWrongWordOfTwo_IsHalf()
        => Assert.Equal(0.5, OcrAccuracy.WordErrorRate("the cat", "the dog"), 6);

    [Fact]
    public void Wer_ExtraWord_CountsAsInsertion()
        => Assert.Equal(0.5, OcrAccuracy.WordErrorRate("two words", "two extra words"), 6);

    [Fact]
    public void Wer_IgnoresWhitespaceRuns()
        => Assert.Equal(0.0, OcrAccuracy.WordErrorRate("a  b\tc", "a b c"), 6);
}
