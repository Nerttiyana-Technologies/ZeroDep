using System;
using Xunit;
using ZeroDep.Abstractions;
using ZeroDep.Ocr;

namespace ZeroDep.Ocr.Tests;

/// <summary>
/// O1 bridge: a decoded JPEG becomes a <see cref="DecodedImage"/> that an <see cref="IOcrEngine"/>
/// can consume. No real engine is involved — a fake engine stands in.
/// </summary>
public sealed class OcrImageConverterTests
{
    // 8x8 grayscale baseline JPEG, solid gray(128).
    private const string Gray8 =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/wAALCAAIAAgBAREA/8QAFAABAAAAAAAAAAAAAAAAAAAAAP/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AP//Z";

    private sealed class FakeOcrEngine : IOcrEngine
    {
        public OcrResult Recognize(DecodedImage image, OcrOptions options)
            => new OcrResult
            {
                Lines = new[]
                {
                    new OcrLine
                    {
                        Text = $"{image.Width}x{image.Height}@{image.Dpi}",
                        Confidence = 0.95,
                        Bounds = new BoundingBox(0, 0, image.Width, image.Height),
                    },
                },
            };
    }

    [Fact]
    public void FromJpeg_ProducesDecodedImage_AndDrivesEngine()
    {
        DecodedImage image = OcrImageConverter.FromJpeg(Convert.FromBase64String(Gray8), dpi: 150);

        Assert.Equal(8, image.Width);
        Assert.Equal(8, image.Height);
        Assert.Equal(150, image.Dpi);
        Assert.Equal(PixelFormat.Gray8, image.Format);
        Assert.Equal(8 * 8, image.Pixels.Length);

        OcrResult result = new FakeOcrEngine().Recognize(image, new OcrOptions { Languages = new[] { "eng" } });
        OcrLine line = Assert.Single(result.Lines);
        Assert.Equal("8x8@150", line.Text);
    }
}
