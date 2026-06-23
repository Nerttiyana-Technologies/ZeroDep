using System.Text;
using Xunit;
using ZeroDep.Objects;

namespace ZeroDep.Objects.Tests;

public sealed class PdfObjectParserTests
{
    private static PdfObjectParser Parser(string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        return new PdfObjectParser(bytes, 0, bytes.Length);
    }

    [Fact]
    public void ParsesDictionaryWithReference()
    {
        PdfObject obj = Parser("<< /Type /Catalog /Pages 3 0 R /Count 2 >>").ParseValue();

        PdfDictionary dict = Assert.IsType<PdfDictionary>(obj);
        Assert.Equal("Catalog", Assert.IsType<PdfName>(dict["Type"]!).Value);

        PdfReference pages = Assert.IsType<PdfReference>(dict["Pages"]!);
        Assert.Equal(3, pages.ObjectNumber);
        Assert.Equal(0, pages.Generation);

        Assert.Equal(2L, Assert.IsType<PdfInteger>(dict["Count"]!).Value);
    }

    [Fact]
    public void ParsesArrayOfMixedValues()
    {
        PdfObject obj = Parser("[1 2.5 (x) /N true null]").ParseValue();

        PdfArray array = Assert.IsType<PdfArray>(obj);
        Assert.Equal(6, array.Count);
        Assert.Equal(1L, Assert.IsType<PdfInteger>(array[0]).Value);
        Assert.Equal(2.5, Assert.IsType<PdfReal>(array[1]).Value, 5);
        Assert.Equal("x", Assert.IsType<PdfString>(array[2]).GetText());
        Assert.Equal("N", Assert.IsType<PdfName>(array[3]).Value);
        Assert.Same(PdfBoolean.True, array[4]);
        Assert.Same(PdfNull.Instance, array[5]);
    }

    [Fact]
    public void ParsesStreamRawBytes()
    {
        PdfObject obj = Parser("<< /Length 5 >>\nstream\nABCDE\nendstream").ParseValue();

        PdfStream stream = Assert.IsType<PdfStream>(obj);
        Assert.Equal("ABCDE", Encoding.ASCII.GetString(stream.GetRawBytes()));
        Assert.Equal(5L, Assert.IsType<PdfInteger>(stream.Dictionary["Length"]!).Value);
    }

    [Fact]
    public void DistinguishesIntegerFromReference()
    {
        PdfObject obj = Parser("42").ParseValue();
        Assert.Equal(42L, Assert.IsType<PdfInteger>(obj).Value);
    }
}
