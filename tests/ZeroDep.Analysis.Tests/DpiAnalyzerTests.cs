using System.IO;
using System.Text;
using Xunit;
using ZeroDep.Abstractions;
using ZeroDep.Analysis;

namespace ZeroDep.Analysis.Tests;

public sealed class DpiAnalyzerTests
{
    [Fact]
    public void ComputesEffectiveDpiAndFlagsBelowThreshold()
    {
        // 100x100 px image drawn at 144x72 pt (2 in x 1 in) -> 50 / 100 DPI.
        byte[] pdf = BuildSingleImagePdf("q 144 0 0 72 0 0 cm /Im0 Do Q");

        var images = DpiAnalyzer.Analyze(new MemoryStream(pdf), threshold: 150);

        ImageDpiInfo image = Assert.Single(images);
        Assert.Equal(0, image.PageIndex);
        Assert.Equal("Im0", image.ResourceName);
        Assert.Equal(100, image.PixelWidth);
        Assert.Equal(100, image.PixelHeight);
        Assert.Equal(144, image.RenderedWidthPoints, 3);
        Assert.Equal(72, image.RenderedHeightPoints, 3);
        Assert.Equal(50, image.HorizontalDpi, 3);
        Assert.Equal(100, image.VerticalDpi, 3);
        Assert.Equal(50, image.EffectiveDpi, 3);
        Assert.True(image.IsBelowThreshold);
    }

    [Fact]
    public void DoesNotFlagWhenAboveThreshold()
    {
        byte[] pdf = BuildSingleImagePdf("q 144 0 0 72 0 0 cm /Im0 Do Q");
        var images = DpiAnalyzer.Analyze(new MemoryStream(pdf), threshold: 30);
        Assert.False(Assert.Single(images).IsBelowThreshold); // effective 50 >= 30
    }

    // Foliant lesson (SF-1449 / scan-quality): the LIMITING (smaller) axis governs legibility,
    // so anamorphic scaling must report min(dpiX, dpiY) as the effective DPI.
    [Fact]
    public void EffectiveDpiUsesLimitingAxis()
    {
        // 100x100 px drawn at 36 pt wide (high DPI) x 288 pt tall (low DPI):
        //   dpiX = 100*72/36  = 200,  dpiY = 100*72/288 = 25  ->  effective = 25.
        byte[] pdf = BuildSingleImagePdf("q 36 0 0 288 0 0 cm /Im0 Do Q");

        ImageDpiInfo image = Assert.Single(DpiAnalyzer.Analyze(new MemoryStream(pdf), threshold: 150));

        Assert.Equal(200, image.HorizontalDpi, 3);
        Assert.Equal(25, image.VerticalDpi, 3);
        Assert.Equal(25, image.EffectiveDpi, 3);     // the worse axis, not the average or the max
        Assert.True(image.IsBelowThreshold);
    }

    private static byte[] BuildSingleImagePdf(string content)
    {
        var ms = new MemoryStream();
        void W(string s) { byte[] b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }

        W("%PDF-1.4\n");
        long o1 = ms.Position; W("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        long o2 = ms.Position; W("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] >>\nendobj\n");
        long o3 = ms.Position; W("3 0 obj\n<< /Type /Page /Parent 2 0 R /Resources << /XObject << /Im0 5 0 R >> >> /Contents 4 0 R >>\nendobj\n");
        long o4 = ms.Position; W($"4 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");
        long o5 = ms.Position; W("5 0 obj\n<< /Type /XObject /Subtype /Image /Width 100 /Height 100 /BitsPerComponent 8 /Length 0 >>\nstream\nendstream\nendobj\n");

        long xref = ms.Position;
        W("xref\n0 6\n");
        W("0000000000 65535 f \n");
        W($"{o1:D10} 00000 n \n");
        W($"{o2:D10} 00000 n \n");
        W($"{o3:D10} 00000 n \n");
        W($"{o4:D10} 00000 n \n");
        W($"{o5:D10} 00000 n \n");
        W("trailer\n<< /Size 6 /Root 1 0 R >>\n");
        W($"startxref\n{xref}\n%%EOF");
        return ms.ToArray();
    }
}
