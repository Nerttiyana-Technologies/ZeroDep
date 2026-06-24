using System.Collections.Generic;
using Xunit;
using ZeroDep.Abstractions;
using ZeroDep.Ocr;

namespace ZeroDep.Ocr.Tests;

/// <summary>
/// Contract-level tests for the OCR abstraction (milestone O0): an <see cref="IOcrEngine"/>
/// implementation is driven through <see cref="DecodedImage"/> / <see cref="OcrOptions"/> and returns
/// an <see cref="OcrResult"/>, with the confidence threshold honored. No real engine is involved.
/// </summary>
public sealed class OcrContractTests
{
    // A deterministic stand-in engine: emits the image dimensions as one line at fixed confidence.
    private sealed class FakeOcrEngine : IOcrEngine
    {
        public OcrResult Recognize(DecodedImage image, OcrOptions options)
        {
            var line = new OcrLine
            {
                Text = image.Width + "x" + image.Height,
                Confidence = 0.9,
                Bounds = new BoundingBox(0, 0, image.Width, image.Height),
            };

            var lines = new List<OcrLine>();
            if (line.Confidence >= options.MinConfidence)
            {
                lines.Add(line);
            }

            return new OcrResult { Lines = lines };
        }
    }

    [Fact]
    public void Engine_Recognizes_AndHonorsConfidenceThreshold()
    {
        IOcrEngine engine = new FakeOcrEngine();
        var image = new DecodedImage
        {
            Width = 200,
            Height = 100,
            Dpi = 150,
            Format = PixelFormat.Gray8,
            Pixels = new byte[200 * 100],
        };

        OcrResult kept = engine.Recognize(image, new OcrOptions { Languages = new[] { "eng" }, MinConfidence = 0.5 });
        OcrLine line = Assert.Single(kept.Lines);
        Assert.Equal("200x100", line.Text);
        Assert.Equal(0.9, line.Confidence, 3);
        Assert.Equal(200, line.Bounds.Width, 3);
        Assert.Equal(100, line.Bounds.Height, 3);

        OcrResult dropped = engine.Recognize(image, new OcrOptions { MinConfidence = 0.95 });
        Assert.Empty(dropped.Lines);
    }

    [Fact]
    public void DecodedImage_DefaultsAreSafe()
    {
        var image = new DecodedImage();
        Assert.Equal(PixelFormat.Gray8, image.Format);
        Assert.NotNull(image.Pixels);
        Assert.Empty(image.Pixels);
    }
}
