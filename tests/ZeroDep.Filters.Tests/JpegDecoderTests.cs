using System;
using Xunit;
using ZeroDep.Filters;

namespace ZeroDep.Filters.Tests;

/// <summary>
/// Stage 2 of the JPEG (/DCTDecode) decoder: full baseline pixel decode. Fixtures are tiny baseline
/// JPEGs of known solid colors; expected center-pixel values are the ground truth a correct decoder
/// produces (verified independently). A small tolerance absorbs IDCT rounding and JPEG quantization.
/// </summary>
public sealed class JpegDecoderTests
{
    // 8x8 grayscale, solid gray(128). Single component.
    private const string Gray8 =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/wAALCAAIAAgBAREA/8QAFAABAAAAAAAAAAAAAAAAAAAAAP/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AP//Z";

    // 16x16 RGB, solid srgb(200,30,30), 4:4:4.
    private const string Red444 =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/2wBDAQMDAwQDBAgEBAgQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/wAARCAAQABADAREAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAf/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFgEBAQEAAAAAAAAAAAAAAAAAAAcI/8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAwDAQACEQMRAD8AgSZNxgAAP//Z";

    // 16x16 RGB, solid srgb(30,80,200), 4:2:0 (chroma subsampled).
    private const string Blue420 =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/2wBDAQMDAwQDBAgEBAgQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/wAARCAAQABADASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAj/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAABwj/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCfAFaiJ//Z";

    [Fact]
    public void DecodesGrayscaleBaseline()
    {
        RasterImage img = JpegDecoder.Decode(Convert.FromBase64String(Gray8));

        Assert.Equal(8, img.Width);
        Assert.Equal(8, img.Height);
        Assert.Equal(1, img.Components);
        Assert.Equal(8 * 8, img.Samples.Length);
        Assert.InRange(GrayAt(img, 4, 4), 128 - 12, 128 + 12);
    }

    [Fact]
    public void DecodesRgbBaseline_444()
    {
        RasterImage img = JpegDecoder.Decode(Convert.FromBase64String(Red444));

        Assert.Equal(16, img.Width);
        Assert.Equal(16, img.Height);
        Assert.Equal(3, img.Components);
        (int r, int g, int b) = RgbAt(img, 8, 8);
        Assert.InRange(r, 180, 220);
        Assert.InRange(g, 10, 55);
        Assert.InRange(b, 10, 55);
    }

    // 16x16 CMYK (Adobe APP14, inverted), solid srgb(200,30,30).
    private const string CmykRed =
        "/9j/7gAOQWRvYmUAZAAAAAAC/9sAQwADAgICAgIDAgICAwMDAwQGBAQEBAQIBgYFBgkICgoJCAkJCgwPDAoLDgsJCQ0RDQ4PEBAREAoMEhMSEBMPEBAQ/9sAQwEDAwMEAwQIBAQIEAsJCxAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQ/8AAFAgAEAAQBAERAAIRAQMRAQQRAP/EABYAAQEBAAAAAAAAAAAAAAAAAAAHCP/EABQQAQAAAAAAAAAAAAAAAAAAAAD/xAAWAQEBAQAAAAAAAAAAAAAAAAAABwn/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADgQBAAIRAxEEAAA/AKAsbNxsAAAAB//Z";

    [Fact]
    public void DecodesCmykBaseline_AdobeInverted()
    {
        RasterImage img = JpegDecoder.Decode(Convert.FromBase64String(CmykRed));

        Assert.Equal(16, img.Width);
        Assert.Equal(16, img.Height);
        Assert.Equal(3, img.Components);   // CMYK is converted to RGB
        (int r, int g, int b) = RgbAt(img, 8, 8);
        Assert.True(
            r is >= 180 and <= 220 && g is >= 10 and <= 55 && b is >= 10 and <= 55,
            $"CMYK→RGB decoded ({r},{g},{b}); expected ~(200,30,29)");
    }

    [Fact]
    public void DecodesRgbBaseline_420Subsampled()
    {
        RasterImage img = JpegDecoder.Decode(Convert.FromBase64String(Blue420));

        Assert.Equal(16, img.Width);
        Assert.Equal(16, img.Height);
        Assert.Equal(3, img.Components);
        (int r, int g, int b) = RgbAt(img, 8, 8);
        Assert.InRange(r, 10, 55);
        Assert.InRange(g, 60, 100);
        Assert.InRange(b, 175, 220);
    }

    // 16x16 RGB progressive (10 scans), solid srgb(60,160,220).
    private const string ProgSolid =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/2wBDAQMDAwQDBAgEBAgQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/wgARCAAQABADAREAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAT/xAAWAQEBAQAAAAAAAAAAAAAAAAAABgf/2gAMAwEAAhADEAAAAbL7NQB//8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBAQABBQIf/8QAFBEBAAAAAAAAAAAAAAAAAAAAIP/aAAgBAwEBPwEf/8QAFBEBAAAAAAAAAAAAAAAAAAAAIP/aAAgBAgEBPwEf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBAQAGPwIf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBAQABPyEf/9oADAMBAAIAAwAAABBtv//EABQRAQAAAAAAAAAAAAAAAAAAACD/2gAIAQMBAT8QH//EABQRAQAAAAAAAAAAAAAAAAAAACD/2gAIAQIBAT8QH//EABQQAQAAAAAAAAAAAAAAAAAAACD/2gAIAQEAAT8QH//Z";

    // 32x32 grayscale progressive (6 scans), black→white gradient (exercises AC scans).
    private const string ProgGrad =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/wgALCAAgACABAREA/8QAFgABAQEAAAAAAAAAAAAAAAAAAAcI/9oACAEBAAAAAc9CsCsCsD//xAAVEAEBAAAAAAAAAAAAAAAAAAAAFf/aAAgBAQABBQKampqampqampqampqampqa/8QAFhABAQEAAAAAAAAAAAAAAAAAADEy/9oACAEBAAY/AssssoiIiIjLLLL/xAAUEAEAAAAAAAAAAAAAAAAAAABA/9oACAEBAAE/IQVX/wD9V//aAAgBAQAAABD/APD/xAAYEAACAwAAAAAAAAAAAAAAAAAAEXGR8P/aAAgBAQABPxCaiaiaiajaNo2jaNo2jaNoioioioio/9k=";

    [Fact]
    public void DecodesProgressiveRgb()
    {
        RasterImage img = JpegDecoder.Decode(Convert.FromBase64String(ProgSolid));

        Assert.Equal(16, img.Width);
        Assert.Equal(16, img.Height);
        Assert.Equal(3, img.Components);
        (int r, int g, int b) = RgbAt(img, 8, 8);
        Assert.True(
            r is >= 40 and <= 85 && g is >= 138 and <= 182 && b is >= 198 and <= 242,
            $"progressive RGB decoded ({r},{g},{b}); expected ~(60,160,220)");
    }

    [Fact]
    public void DecodesProgressiveGrayscaleGradient()
    {
        RasterImage img = JpegDecoder.Decode(Convert.FromBase64String(ProgGrad));

        Assert.Equal(32, img.Width);
        Assert.Equal(32, img.Height);
        Assert.Equal(1, img.Components);
        Assert.InRange(GrayAt(img, 8, 8), 66 - 22, 66 + 22);
    }

    private static int GrayAt(RasterImage img, int x, int y) => img.Samples[(y * img.Width) + x];

    private static (int R, int G, int B) RgbAt(RasterImage img, int x, int y)
    {
        int o = ((y * img.Width) + x) * 3;
        return (img.Samples[o], img.Samples[o + 1], img.Samples[o + 2]);
    }
}
