using System.Text.Json;
using Xunit;
using ZeroDep.Abstractions;
using ZeroDep.Json;

namespace ZeroDep.Json.Tests;

public sealed class DocumentJsonTests
{
    [Fact]
    public void SerializesValidJsonWithEscaping()
    {
        var analysis = new DocumentAnalysis
        {
            PageCount = 2,
            Images = new[]
            {
                new ImageDpiInfo { PageIndex = 0, ResourceName = "Im0", PixelWidth = 100, PixelHeight = 100, EffectiveDpi = 50, Threshold = 150, IsBelowThreshold = true },
            },
            TextRuns = new[]
            {
                new TextRunInfo { PageIndex = 0, Text = "line \"with\" quotes\nand newline", X = 10, Y = 20, Width = 30, FontSize = 12 },
            },
            Form = new AcroFormReport
            {
                HasAcroForm = true,
                Fields = new[] { new FormFieldInfo { FullyQualifiedName = "a.b", FieldType = "Tx", Value = "V", PageIndex = 0 } },
            },
            Coverage = new[]
            {
                new CoverageItem { Id = "t0", Kind = "text", Value = "hello", Page = 0, Bounds = new BoundingBox(1, 2, 3, 4) },
            },
            Pages = new[]
            {
                new PageClassification
                {
                    PageIndex = 0,
                    Class = PageContentClass.DigitalText,
                    Confidence = 0.9,
                    Signals = new PageSignals { TextRunCount = 12, RulingLineCount = 3, FontDistinctCount = 2, TextCoverageFraction = 0.25 },
                },
            },
        };

        string json = DocumentJson.Write(analysis, indent: true);

        using JsonDocument doc = JsonDocument.Parse(json); // valid JSON is the primary assertion
        JsonElement root = doc.RootElement;
        Assert.Equal("1.2", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(2, root.GetProperty("pageCount").GetInt32());

        JsonElement page = root.GetProperty("pages")[0];
        Assert.Equal("DigitalText", page.GetProperty("class").GetString());
        Assert.Equal(0.9, page.GetProperty("confidence").GetDouble());
        Assert.Equal(3, page.GetProperty("signals").GetProperty("rulingLineCount").GetInt32());

        JsonElement image = root.GetProperty("images")[0];
        Assert.Equal(50, image.GetProperty("effectiveDpi").GetDouble());
        Assert.True(image.GetProperty("belowThreshold").GetBoolean());

        JsonElement run = root.GetProperty("textRuns")[0];
        Assert.Equal("line \"with\" quotes\nand newline", run.GetProperty("text").GetString()); // escaping round-trips

        JsonElement field = root.GetProperty("form").GetProperty("fields")[0];
        Assert.Equal("a.b", field.GetProperty("fullyQualifiedName").GetString());

        JsonElement cover = root.GetProperty("coverage")[0];
        Assert.Equal("t0", cover.GetProperty("id").GetString());
        Assert.Equal(3, cover.GetProperty("bounds").GetProperty("width").GetDouble());
    }
}
