using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using ZeroDep.Content;
using ZeroDep.Objects;

namespace ZeroDep.Content.Tests;

/// <summary>
/// Feature B (M4) text-layer trust edge cases from the Foliant lessons: a CID font without a
/// <c>/ToUnicode</c> map must never emit control bytes or <c>(cid:N)</c> garbage; an unresolved
/// <c>/Differences</c> glyph name yields empty (flagged) text, not a wrong character; and extracted
/// runs preserve content-stream order (recall is order-blind — order must be asserted).
/// </summary>
public sealed class TextLayerEdgeCaseTests
{
    private static PdfDictionary Dict(params (string Key, PdfObject Value)[] entries)
    {
        var map = new Dictionary<string, PdfObject>();
        foreach (var (key, value) in entries) map[key] = value;
        return new PdfDictionary(map);
    }

    private static PdfArray Arr(params PdfObject[] items) => new PdfArray(items);

    private static FontInfo Font(PdfDictionary dict) => new FontInfo(dict, o => o, s => s.GetRawBytes());

    private static string TextOf(IEnumerable<Glyph> glyphs) => string.Concat(glyphs.Select(g => g.Text));

    // B2: Identity-H CID font with NO /ToUnicode -> undecodable, but never control bytes / (cid:N).
    [Fact]
    public void CidFontWithoutToUnicode_EmitsNoControlBytes()
    {
        PdfDictionary descendant = Dict(("Subtype", new PdfName("CIDFontType2")));
        PdfDictionary font = Dict(
            ("Subtype", new PdfName("Type0")),
            ("Encoding", new PdfName("Identity-H")),
            ("DescendantFonts", Arr(descendant)));

        List<Glyph> glyphs = Font(font).Decode(new byte[] { 0x00, 0x01, 0x00, 0x02 });
        string text = TextOf(glyphs);

        Assert.Equal(string.Empty, text);            // undecodable -> empty, not garbage
        Assert.DoesNotContain(text, char.IsControl); // never C0/C1 control bytes / (cid:N)
    }

    // B3: a /Differences glyph name that cannot be resolved yields empty text, not a wrong glyph.
    [Fact]
    public void UnresolvedDifferencesGlyph_IsEmptyNotGarbage()
    {
        PdfDictionary encoding = Dict(
            ("Differences", Arr(new PdfInteger(1), new PdfName("A"), new PdfName("notarealglyphname999"))));
        PdfDictionary font = Dict(("Subtype", new PdfName("Type1")), ("Encoding", encoding));

        List<Glyph> glyphs = Font(font).Decode(new byte[] { 1, 2 });

        Assert.Equal("A", glyphs[0].Text);          // resolved
        Assert.Equal(string.Empty, glyphs[1].Text); // unresolved -> empty (flagged), no fabrication
    }

    // B5: extracted runs preserve content-stream order (membership alone is not enough).
    [Fact]
    public void TextRuns_PreserveContentStreamOrder()
    {
        PdfDictionary resources = Dict(("Font", Dict(("F1", Dict(
            ("Type", new PdfName("Font")), ("Subtype", new PdfName("Type1")), ("BaseFont", new PdfName("Helvetica")))))));
        var interpreter = new ContentInterpreter(o => o, s => s.GetRawBytes());

        ContentResult result = interpreter.RunAll(
            Encoding.ASCII.GetBytes("BT /F1 12 Tf 100 700 Td (First) Tj 0 -20 Td (Second) Tj 0 -20 Td (Third) Tj ET"),
            resources,
            Matrix.Identity);

        Assert.Equal(new[] { "First", "Second", "Third" }, result.TextRuns.Select(r => r.Text).ToArray());
    }
}
