using System.Collections.Generic;
using Xunit;
using ZeroDep.Abstractions;
using ZeroDep.Analysis;

namespace ZeroDep.Analysis.Tests;

public sealed class PlainTextSpacingTests
{
    [Fact]
    public void JoinsContiguousRunsAndSpacesRealGaps()
    {
        // "SOLICI" + "TATION" are contiguous (no gap) -> one word; "WORD" is a real gap away.
        var runs = new List<TextRunInfo>
        {
            new TextRunInfo { PageIndex = 0, Text = "SOLICI", X = 0, Y = 700, Width = 30, FontSize = 12 },
            new TextRunInfo { PageIndex = 0, Text = "TATION", X = 30, Y = 700, Width = 30, FontSize = 12 },
            new TextRunInfo { PageIndex = 0, Text = "WORD", X = 100, Y = 700, Width = 20, FontSize = 12 },
        };

        string plain = TextAnalyzer.BuildPlainText(runs);
        Assert.Contains("SOLICITATION WORD", plain);
        Assert.DoesNotContain("SOLICI TATION", plain);
    }
}
