using System.Collections.Generic;
using System.Text;
using Xunit;
using ZeroDep.Content;
using ZeroDep.Objects;

namespace ZeroDep.Content.Tests;

/// <summary>
/// ADR-0003 P1 signals gathered by <see cref="ContentInterpreter"/>: axis-aligned ruling-line counting
/// from path operators (m/l/re), and distinct-font counting from Tf.
/// </summary>
public sealed class RulingLineTests
{
    private static PdfDictionary Empty() => new PdfDictionary(new Dictionary<string, PdfObject>());

    private static ContentInterpreter Interpreter() => new ContentInterpreter(o => o, s => s.GetRawBytes());

    [Fact]
    public void CountsRectangleSidesAndLinesAsRulings()
    {
        // a 100x100 rectangle (4 axis-aligned sides) + one 190pt horizontal line, both painted
        byte[] content = Encoding.ASCII.GetBytes("0 0 100 100 re S 10 10 m 200 10 l S");
        ContentResult result = Interpreter().RunAll(content, Empty(), Matrix.Identity);
        Assert.Equal(5, result.RulingLineCount);
    }

    [Fact]
    public void ShortAndDiagonalSegmentsAreNotRulings()
    {
        // a 5pt segment (too short) and a diagonal line (not axis-aligned) → no rulings
        byte[] content = Encoding.ASCII.GetBytes("0 0 m 5 0 l S 0 0 m 100 100 l S");
        ContentResult result = Interpreter().RunAll(content, Empty(), Matrix.Identity);
        Assert.Equal(0, result.RulingLineCount);
    }

    [Fact]
    public void LargeFilledRectangleIsNotARuling()
    {
        // a 200x200 filled background rectangle must not count as ruling lines
        byte[] content = Encoding.ASCII.GetBytes("0 0 200 200 re f");
        ContentResult result = Interpreter().RunAll(content, Empty(), Matrix.Identity);
        Assert.Equal(0, result.RulingLineCount);
    }

    [Fact]
    public void ThinFilledRectangleCountsAsRulings()
    {
        // a 200x2 thin filled rectangle (a drawn rule) counts its two long sides
        byte[] content = Encoding.ASCII.GetBytes("0 0 200 2 re f");
        ContentResult result = Interpreter().RunAll(content, Empty(), Matrix.Identity);
        Assert.Equal(2, result.RulingLineCount);
    }

    [Fact]
    public void CountsDistinctFonts()
    {
        byte[] content = Encoding.ASCII.GetBytes("/F1 12 Tf /F2 10 Tf /F1 8 Tf");
        ContentResult result = Interpreter().RunAll(content, Empty(), Matrix.Identity);
        Assert.Equal(2, result.FontNames.Count);
    }
}
