using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using ZeroDep.Abstractions;
using ZeroDep.Analysis;

namespace ZeroDep.Analysis.Tests;

/// <summary>
/// Feature D (M6) imaging hardening from the Foliant lessons: small assets (logos/stamps) carry a
/// small <see cref="ImageDpiInfo.PageAreaFraction"/> so they can be excluded from scan-quality
/// judgments; inline images (BI/ID/EI) get DPI computed; and a stamp/watermark overprint does not
/// corrupt the metrics of the page image it overlays.
/// </summary>
public sealed class ImagingHardeningTests
{
    // D1: a low-DPI logo and a low-DPI full-page scan are both below threshold, but the page-area
    // fraction lets a consumer ignore the logo when judging scan quality.
    [Fact]
    public void SmallLowDpiLogo_IsDistinguishedFromFullPageScan_ByAreaFraction()
    {
        byte[] pdf = BuildImagesPdf(
            "q 612 0 0 792 0 0 cm /Scan Do Q q 36 0 0 36 0 0 cm /Logo Do Q",
            ("Scan", 100, 100),   // 100px over the whole page -> very low DPI, area ~1.0
            ("Logo", 10, 10));    // 10px in a 36pt box -> low DPI, but tiny area

        var images = DpiAnalyzer.Analyze(new MemoryStream(pdf), threshold: 150);

        ImageDpiInfo scan = images.Single(i => i.ResourceName == "Scan");
        ImageDpiInfo logo = images.Single(i => i.ResourceName == "Logo");

        Assert.True(scan.IsBelowThreshold);
        Assert.True(logo.IsBelowThreshold);
        Assert.True(scan.PageAreaFraction >= 0.5);
        Assert.True(logo.PageAreaFraction < 0.5);

        // Scan-quality view: below threshold AND covering a significant area -> only the scan.
        var scanQualityConcerns = images.Where(i => i.IsBelowThreshold && i.PageAreaFraction >= 0.5).ToList();
        Assert.Equal(new[] { "Scan" }, scanQualityConcerns.Select(i => i.ResourceName).ToArray());
    }

    // D2: an inline image (BI/ID/EI) has its DPI computed from /W,/H and the CTM.
    [Fact]
    public void InlineImage_DpiIsComputed()
    {
        byte[] pdf = BuildImagesPdf("q 144 0 0 72 0 0 cm BI /W 100 /H 100 /BPC 8 ID \x01\x02\x03 EI Q");

        ImageDpiInfo image = Assert.Single(DpiAnalyzer.Analyze(new MemoryStream(pdf), threshold: 150));

        Assert.Equal("inline", image.ResourceName);
        Assert.Equal(100, image.PixelWidth);
        Assert.Equal(100, image.PixelHeight);
        Assert.Equal(50, image.EffectiveDpi, 3);   // min(100*72/144, 100*72/72) = min(50,100)
        Assert.True(image.IsBelowThreshold);
    }

    // D3: a stamp drawn over a high-DPI page image does not corrupt the page image's metrics.
    [Fact]
    public void StampOverprint_DoesNotCorruptUnderlyingImageMetrics()
    {
        byte[] pdf = BuildImagesPdf(
            "q 612 0 0 792 0 0 cm /Scan Do Q q 72 0 0 72 0 0 cm /Stamp Do Q",
            ("Scan", 1700, 2200),   // 200 DPI full page (good quality)
            ("Stamp", 50, 50));     // low-DPI stamp overlay

        var images = DpiAnalyzer.Analyze(new MemoryStream(pdf), threshold: 150);

        ImageDpiInfo scan = images.Single(i => i.ResourceName == "Scan");
        ImageDpiInfo stamp = images.Single(i => i.ResourceName == "Stamp");

        Assert.Equal(200, scan.HorizontalDpi, 3);
        Assert.Equal(200, scan.VerticalDpi, 3);
        Assert.False(scan.IsBelowThreshold);     // unaffected by the overlapping stamp
        Assert.True(stamp.IsBelowThreshold);
    }

    private static byte[] BuildImagesPdf(string content, params (string Name, int W, int H)[] images)
    {
        var ms = new MemoryStream();
        void W(string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s);
            ms.Write(b, 0, b.Length);
        }

        int contentLength = Encoding.ASCII.GetBytes(content).Length;
        var xobject = new StringBuilder();
        for (int i = 0; i < images.Length; i++)
        {
            xobject.Append($"/{images[i].Name} {5 + i} 0 R ");
        }

        var offsets = new List<long>();
        W("%PDF-1.5\n");
        offsets.Add(ms.Position); W("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets.Add(ms.Position); W("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] >>\nendobj\n");
        offsets.Add(ms.Position); W($"3 0 obj\n<< /Type /Page /Parent 2 0 R /Resources << /XObject << {xobject} >> >> /Contents 4 0 R >>\nendobj\n");
        offsets.Add(ms.Position); W($"4 0 obj\n<< /Length {contentLength} >>\nstream\n{content}\nendstream\nendobj\n");
        for (int i = 0; i < images.Length; i++)
        {
            offsets.Add(ms.Position);
            W($"{5 + i} 0 obj\n<< /Type /XObject /Subtype /Image /Width {images[i].W} /Height {images[i].H} /BitsPerComponent 8 /Length 0 >>\nstream\nendstream\nendobj\n");
        }

        long xref = ms.Position;
        int count = offsets.Count;
        W($"xref\n0 {count + 1}\n0000000000 65535 f \n");
        foreach (long o in offsets)
        {
            W($"{o:D10} 00000 n \n");
        }

        W($"trailer\n<< /Size {count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        return ms.ToArray();
    }
}
