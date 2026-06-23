using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using ZeroDep.Content;
using ZeroDep.Objects;

namespace ZeroDep.Content.Tests;

public sealed class FontDecodingTests
{
    private static PdfDictionary Dict(params (string Key, PdfObject Value)[] entries)
    {
        var map = new Dictionary<string, PdfObject>();
        foreach (var (key, value) in entries) map[key] = value;
        return new PdfDictionary(map);
    }

    private static PdfArray Arr(params PdfObject[] items) => new PdfArray(items);

    private static PdfStream CMap(string text)
    {
        byte[] b = Encoding.ASCII.GetBytes(text);
        return new PdfStream(Dict(("Length", new PdfInteger(b.Length))), b);
    }

    private static string TextOf(IEnumerable<Glyph> glyphs) => string.Concat(glyphs.Select(g => g.Text));

    [Fact]
    public void DecodesViaToUnicodeBfChar()
    {
        PdfDictionary font = Dict(
            ("Subtype", new PdfName("Type1")),
            ("ToUnicode", CMap("2 beginbfchar <41> <0041> <42> <0042> endbfchar")));

        var glyphs = new FontInfo(font, o => o, s => s.GetRawBytes()).Decode(new byte[] { 0x41, 0x42 });
        Assert.Equal("AB", TextOf(glyphs));
    }

    [Fact]
    public void UsesSimpleWidthsArray()
    {
        PdfDictionary font = Dict(
            ("Subtype", new PdfName("Type1")),
            ("FirstChar", new PdfInteger(65)),
            ("Widths", Arr(new PdfInteger(600), new PdfInteger(700))));

        var glyphs = new FontInfo(font, o => o, s => s.GetRawBytes()).Decode(new byte[] { 65, 66 });
        Assert.Equal("AB", TextOf(glyphs));
        Assert.Equal(600, glyphs[0].WidthEm, 3);
        Assert.Equal(700, glyphs[1].WidthEm, 3);
    }

    [Fact]
    public void DecodesType0WithCidWidthsAndDefault()
    {
        PdfDictionary descendant = Dict(
            ("Subtype", new PdfName("CIDFontType2")),
            ("DW", new PdfInteger(1000)),
            ("W", Arr(new PdfInteger(1), Arr(new PdfInteger(500))))); // CID 1 -> width 500

        PdfDictionary font = Dict(
            ("Subtype", new PdfName("Type0")),
            ("Encoding", new PdfName("Identity-H")),
            ("ToUnicode", CMap("1 beginbfrange <0001> <0002> <0041> endbfrange")),
            ("DescendantFonts", Arr(descendant)));

        var glyphs = new FontInfo(font, o => o, s => s.GetRawBytes()).Decode(new byte[] { 0x00, 0x01, 0x00, 0x02 });
        Assert.Equal("AB", TextOf(glyphs));      // range 1..2 -> A, B
        Assert.Equal(500, glyphs[0].WidthEm, 3); // CID 1 from /W
        Assert.Equal(1000, glyphs[1].WidthEm, 3);// CID 2 falls back to /DW
    }

    [Fact]
    public void DecodesViaEncodingDifferences()
    {
        PdfDictionary encoding = Dict(
            ("Differences", Arr(new PdfInteger(1), new PdfName("A"), new PdfName("uni0042"), new PdfName("space"))));
        PdfDictionary font = Dict(
            ("Subtype", new PdfName("Type1")),
            ("Encoding", encoding)); // no /ToUnicode

        var glyphs = new FontInfo(font, o => o, s => s.GetRawBytes()).Decode(new byte[] { 1, 2, 3 });
        Assert.Equal("AB ", TextOf(glyphs)); // A, uni0042 -> B, space
    }
}
