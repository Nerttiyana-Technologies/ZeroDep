using System.Collections.Generic;
using System.Text;
using Xunit;
using ZeroDep.Content;
using ZeroDep.Objects;

namespace ZeroDep.Content.Tests;

public sealed class InlineImageTests
{
    [Fact]
    public void ParsesInlineImageDimensionsAndCtm()
    {
        var resources = new PdfDictionary(new Dictionary<string, PdfObject>());
        var interpreter = new ContentInterpreter(o => o, s => s.GetRawBytes());

        // Inline image drawn at 100x50 pt; binary data between ID and EI is skipped.
        byte[] content = Encoding.ASCII.GetBytes("q 100 0 0 50 0 0 cm BI /W 100 /H 50 /BPC 8 ID \x01\x02\x03 EI Q");

        ContentResult result = interpreter.RunAll(content, resources, Matrix.Identity);

        ImagePlacement image = Assert.Single(result.Images);
        Assert.Equal("inline", image.Name);
        Assert.Equal("Image", Assert.IsType<PdfName>(image.Image.Dictionary["Subtype"]!).Value);
        Assert.Equal(100L, Assert.IsType<PdfInteger>(image.Image.Dictionary["Width"]!).Value);
        Assert.Equal(50L, Assert.IsType<PdfInteger>(image.Image.Dictionary["Height"]!).Value);
        Assert.Equal(100, image.Transform.A, 3);
        Assert.Equal(50, image.Transform.D, 3);
    }
}
