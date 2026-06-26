using System.Collections.Generic;
using Xunit;
using ZeroDep.Color;
using ZeroDep.Objects;

namespace ZeroDep.Color.Tests;

/// <summary>
/// C2 — color-space resolver (ADR-0004 §2.1). Verifies each supported family maps a sample tuple to the
/// expected RGB, including the high-value Indexed (palette) and Separation (tint transform) paths.
/// </summary>
public sealed class PdfColorSpaceTests
{
    private static PdfObject Id(PdfObject o) => o;

    private static byte[] Raw(PdfStream s) => s.GetRawBytes();

    private static PdfArray Arr(params PdfObject[] items) => new PdfArray(items);

    private static PdfDictionary Dict(Dictionary<string, PdfObject> e) => new PdfDictionary(e);

    private static (int R, int G, int B) Rgb(PdfColorSpace cs, params double[] comps)
    {
        cs.ToRgb(comps, 0, out byte r, out byte g, out byte b);
        return (r, g, b);
    }

    [Fact]
    public void DeviceGray_Rgb_Cmyk_ByName()
    {
        Assert.Equal(1, PdfColorSpace.Resolve(new PdfName("DeviceGray"), Id, Raw).ComponentCount);
        Assert.Equal(3, PdfColorSpace.Resolve(new PdfName("DeviceRGB"), Id, Raw).ComponentCount);
        Assert.Equal(4, PdfColorSpace.Resolve(new PdfName("DeviceCMYK"), Id, Raw).ComponentCount);

        Assert.Equal((128, 128, 128), Rgb(DeviceGrayCs(), 0.5019607843));
        Assert.Equal((255, 0, 0), Rgb(PdfColorSpace.Resolve(new PdfName("DeviceRGB"), Id, Raw), 1, 0, 0));
    }

    private static PdfColorSpace DeviceGrayCs() => PdfColorSpace.Resolve(new PdfName("DeviceGray"), Id, Raw);

    [Fact]
    public void DeviceCmyk_WhiteAndCyan()
    {
        PdfColorSpace cmyk = PdfColorSpace.Resolve(new PdfName("DeviceCMYK"), Id, Raw);
        Assert.Equal((255, 255, 255), Rgb(cmyk, 0, 0, 0, 0)); // no ink → white
        (int r, _, int b) = Rgb(cmyk, 1, 0, 0, 0);            // full cyan
        Assert.Equal(0, r);
        Assert.True(b > 200);
    }

    [Fact]
    public void Indexed_LooksUpPalette()
    {
        // [/Indexed /DeviceRGB 1 <lookup>] with two entries
        var lookup = new PdfString(new byte[] { 10, 20, 30, 200, 210, 220 }, isHexString: false);
        PdfArray cs = Arr(new PdfName("Indexed"), new PdfName("DeviceRGB"), new PdfInteger(1), lookup);
        PdfColorSpace indexed = PdfColorSpace.Resolve(cs, Id, Raw);

        Assert.Equal(1, indexed.ComponentCount);
        Assert.Equal((10, 20, 30), Rgb(indexed, 0));
        Assert.Equal((200, 210, 220), Rgb(indexed, 1));
        // index clamps to hival
        Assert.Equal((200, 210, 220), Rgb(indexed, 5));
        // default decode maps raw 8-bit samples to index range
        Assert.Equal(new[] { 0.0, 255.0 }, indexed.DefaultDecode(8));
    }

    [Fact]
    public void Separation_AppliesTintTransform()
    {
        // tint t -> [t,t,t] in DeviceRGB
        var tint = Dict(new Dictionary<string, PdfObject>
        {
            ["FunctionType"] = new PdfInteger(2),
            ["Domain"] = Arr(new PdfReal(0), new PdfReal(1)),
            ["C0"] = Arr(new PdfReal(0), new PdfReal(0), new PdfReal(0)),
            ["C1"] = Arr(new PdfReal(1), new PdfReal(1), new PdfReal(1)),
            ["N"] = new PdfReal(1),
        });
        PdfArray cs = Arr(new PdfName("Separation"), new PdfName("MyInk"), new PdfName("DeviceRGB"), tint);
        PdfColorSpace sep = PdfColorSpace.Resolve(cs, Id, Raw);

        Assert.Equal(1, sep.ComponentCount);
        Assert.Equal((128, 128, 128), Rgb(sep, 0.5));
        Assert.Equal((0, 0, 0), Rgb(sep, 0));
        Assert.Equal((255, 255, 255), Rgb(sep, 1));
    }

    [Fact]
    public void DeviceN_ComponentCountFromNames()
    {
        var tint = Dict(new Dictionary<string, PdfObject>
        {
            ["FunctionType"] = new PdfInteger(4),
            ["Domain"] = Arr(new PdfReal(0), new PdfReal(1), new PdfReal(0), new PdfReal(1)),
            ["Range"] = Arr(new PdfReal(0), new PdfReal(1), new PdfReal(0), new PdfReal(1), new PdfReal(0), new PdfReal(1)),
        });
        // place a trivial program: produce 3 zeros regardless (push 0 0 0)
        var tintStream = new PdfStream((PdfDictionary)tint, System.Text.Encoding.ASCII.GetBytes("{ pop pop 0 0 0 }"));
        PdfArray names = Arr(new PdfName("Ink1"), new PdfName("Ink2"));
        PdfArray cs = Arr(new PdfName("DeviceN"), names, new PdfName("DeviceRGB"), tintStream);
        PdfColorSpace dn = PdfColorSpace.Resolve(cs, Id, Raw);

        Assert.Equal(2, dn.ComponentCount);
        Assert.Equal((0, 0, 0), Rgb(dn, 0.4, 0.6));
    }

    [Fact]
    public void IccBased_UsesAlternateOrN()
    {
        var iccDict = Dict(new Dictionary<string, PdfObject>
        {
            ["N"] = new PdfInteger(3),
            ["Alternate"] = new PdfName("DeviceRGB"),
        });
        var stream = new PdfStream(iccDict, System.Array.Empty<byte>());
        PdfColorSpace icc = PdfColorSpace.Resolve(Arr(new PdfName("ICCBased"), stream), Id, Raw);
        Assert.Equal(3, icc.ComponentCount);
        Assert.Equal((255, 0, 0), Rgb(icc, 1, 0, 0));

        // No alternate → map by /N
        var icc4 = new PdfStream(Dict(new Dictionary<string, PdfObject> { ["N"] = new PdfInteger(4) }), System.Array.Empty<byte>());
        Assert.Equal(4, PdfColorSpace.Resolve(Arr(new PdfName("ICCBased"), icc4), Id, Raw).ComponentCount);
    }

    [Fact]
    public void Lab_BlackAndWhite()
    {
        var labDict = Dict(new Dictionary<string, PdfObject>
        {
            ["WhitePoint"] = Arr(new PdfReal(0.9505), new PdfReal(1.0), new PdfReal(1.089)),
        });
        PdfColorSpace lab = PdfColorSpace.Resolve(Arr(new PdfName("Lab"), labDict), Id, Raw);

        Assert.Equal(3, lab.ComponentCount);
        (int wr, int wg, int wb) = Rgb(lab, 100, 0, 0);
        Assert.True(wr >= 250 && wg >= 250 && wb >= 250);
        Assert.Equal((0, 0, 0), Rgb(lab, 0, 0, 0));
    }

    [Fact]
    public void NamedLookup_ResolvesResourceColorSpace()
    {
        PdfObject? Lookup(string n) => n == "CS0" ? new PdfName("DeviceRGB") : null;
        PdfColorSpace cs = PdfColorSpace.Resolve(new PdfName("CS0"), Id, Raw, Lookup);
        Assert.Equal(3, cs.ComponentCount);
    }

    [Fact]
    public void Pattern_IsNotSupported()
        => Assert.Throws<System.NotSupportedException>(() => PdfColorSpace.Resolve(new PdfName("Pattern"), Id, Raw));
}
