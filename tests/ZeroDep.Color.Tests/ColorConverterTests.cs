using System.Collections.Generic;
using Xunit;
using ZeroDep.Color;
using ZeroDep.Filters;
using ZeroDep.Objects;

namespace ZeroDep.Color.Tests;

/// <summary>
/// C3 — sample→RGB normalizer (ADR-0004 §2.2): bit-depth unpacking (1/2/4/8), row byte-alignment, the
/// <c>/Decode</c> remap, Indexed palette lookup, grayscale passthrough, and CMYK.
/// </summary>
public sealed class ColorConverterTests
{
    private static PdfObject Id(PdfObject o) => o;

    private static byte[] Raw(PdfStream s) => s.GetRawBytes();

    private static PdfColorSpace Cs(string name) => PdfColorSpace.Resolve(new PdfName(name), Id, Raw);

    [Fact]
    public void DeviceRgb_8bit()
    {
        var img = ColorConverter.ToRgb(new byte[] { 255, 0, 0 }, 1, 1, 3, 8, Cs("DeviceRGB"));
        Assert.Equal(3, img.Components);
        Assert.Equal(new byte[] { 255, 0, 0 }, img.Samples);
    }

    [Fact]
    public void DeviceGray_PassesThroughAsGray8()
    {
        var img = ColorConverter.ToRgb(new byte[] { 128 }, 1, 1, 1, 8, Cs("DeviceGray"));
        Assert.Equal(1, img.Components);
        Assert.Equal(new byte[] { 128 }, img.Samples);
    }

    [Fact]
    public void Decode_InvertsGray()
    {
        // Decode [1,0] inverts: raw 0 -> 1.0 -> 255
        var img = ColorConverter.ToRgb(new byte[] { 0 }, 1, 1, 1, 8, Cs("DeviceGray"), new[] { 1.0, 0.0 });
        Assert.Equal(255, img.Samples[0]);
    }

    [Fact]
    public void OneBit_RowByteAligned()
    {
        // width 2, 1 component, 1 bpc: row padded to 1 byte. 0b10000000 -> p0=1(255), p1=0(0)
        var img = ColorConverter.ToRgb(new byte[] { 0b1000_0000 }, 2, 1, 1, 1, Cs("DeviceGray"));
        Assert.Equal(new byte[] { 255, 0 }, img.Samples);
    }

    [Fact]
    public void FourBit_Unpacks()
    {
        // width 2 gray, 4 bpc: 1 byte 0xF0 -> p0=15 (255), p1=0 (0)
        var img = ColorConverter.ToRgb(new byte[] { 0xF0 }, 2, 1, 1, 4, Cs("DeviceGray"));
        Assert.Equal(new byte[] { 255, 0 }, img.Samples);
    }

    [Fact]
    public void TwoRows_PaddingPerRow()
    {
        // width 1, 1bpc, 2 rows: each row is its own padded byte. row0=0x80(1->255), row1=0x00(0)
        var img = ColorConverter.ToRgb(new byte[] { 0x80, 0x00 }, 1, 2, 1, 1, Cs("DeviceGray"));
        Assert.Equal(new byte[] { 255, 0 }, img.Samples);
    }

    [Fact]
    public void Indexed_FromSamples()
    {
        var lookup = new PdfString(new byte[] { 10, 20, 30, 200, 210, 220 }, isHexString: false);
        PdfColorSpace indexed = PdfColorSpace.Resolve(
            new PdfArray(new PdfObject[] { new PdfName("Indexed"), new PdfName("DeviceRGB"), new PdfInteger(1), lookup }),
            Id,
            Raw);

        // 1-bit indexed, width 2: 0b10000000 -> index 1, index 0
        var img = ColorConverter.ToRgb(new byte[] { 0b1000_0000 }, 2, 1, 1, 1, indexed);
        Assert.Equal(3, img.Components);
        Assert.Equal(new byte[] { 200, 210, 220, 10, 20, 30 }, img.Samples);
    }

    [Fact]
    public void Cmyk_WhiteFromRaster()
    {
        var decoded = new RasterImage { Width = 1, Height = 1, Components = 4, Samples = new byte[] { 0, 0, 0, 0 } };
        var img = ColorConverter.ToRgb(decoded, Cs("DeviceCMYK"));
        Assert.Equal(new byte[] { 255, 255, 255 }, img.Samples);
    }
}
