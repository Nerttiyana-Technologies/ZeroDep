using System.Collections.Generic;
using System.Text;
using Xunit;
using ZeroDep.Content;
using ZeroDep.Objects;

namespace ZeroDep.Content.Tests;

public sealed class TextExtractionTests
{
    private static PdfDictionary Dict(params (string Key, PdfObject Value)[] entries)
    {
        var map = new Dictionary<string, PdfObject>();
        foreach (var (key, value) in entries) map[key] = value;
        return new PdfDictionary(map);
    }

    private static PdfDictionary SimpleFontDict()
        => Dict(("Type", new PdfName("Font")), ("Subtype", new PdfName("Type1")), ("BaseFont", new PdfName("Helvetica")));

    private static PdfDictionary Resources()
        => Dict(("Font", Dict(("F1", SimpleFontDict()))));

    private static ContentInterpreter Interpreter()
        => new ContentInterpreter(o => o, s => s.GetRawBytes());

    private static ContentResult Run(string content)
        => Interpreter().RunAll(Encoding.ASCII.GetBytes(content), Resources(), Matrix.Identity);

    [Fact]
    public void ExtractsPositionedTextRun()
    {
        ContentResult result = Run("BT /F1 12 Tf 100 700 Td (Hello World) Tj ET");

        TextRun run = Assert.Single(result.TextRuns);
        Assert.Equal("Hello World", run.Text);
        Assert.Equal(100, run.X, 3);
        Assert.Equal(700, run.Y, 3);
        Assert.Equal(12, run.FontSize, 3);
        Assert.False(run.IsOcrLayer);
    }

    [Fact]
    public void InfersSpaceFromTjKerningGap()
    {
        ContentResult result = Run("BT /F1 12 Tf 0 0 Td [(Hel) -300 (lo)] TJ ET");
        Assert.Equal("Hel lo", Assert.Single(result.TextRuns).Text);
    }

    [Fact]
    public void TagsInvisibleTextAsOcrLayer()
    {
        ContentResult result = Run("BT /F1 12 Tf 3 Tr 50 50 Td (Invisible) Tj ET");

        TextRun run = Assert.Single(result.TextRuns);
        Assert.Equal(3, run.RenderMode);
        Assert.True(run.IsOcrLayer);
    }

    [Fact]
    public void ImagesAndTextDoNotInterfere()
    {
        // Run() still returns images only; RunAll returns both.
        ContentResult result = Run("BT /F1 10 Tf 0 0 Td (text) Tj ET");
        Assert.Single(result.TextRuns);
        Assert.Empty(result.Images);
    }
}
