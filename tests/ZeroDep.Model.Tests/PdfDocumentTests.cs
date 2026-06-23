using System.IO;
using System.Text;
using Xunit;
using ZeroDep.Model;
using ZeroDep.Objects;

namespace ZeroDep.Model.Tests;

public sealed class PdfDocumentTests
{
    [Fact]
    public void BuildsPageListWithInheritedAndOverriddenAttributes()
    {
        byte[] pdf = BuildTwoPagePdf();
        using var document = PdfDocument.Open(new MemoryStream(pdf));

        Assert.Equal(2, document.PageCount);

        PdfPage first = document.Pages[0];
        Assert.Equal(612, first.MediaBox.Width, 3);   // inherited from /Pages
        Assert.Equal(792, first.MediaBox.Height, 3);
        Assert.Equal(0, first.Rotation);
        Assert.NotNull(first.Resources);              // inherited resources
        Assert.True(first.Resources!.ContainsKey("Font"));

        PdfPage second = document.Pages[1];
        Assert.Equal(200, second.MediaBox.Width, 3);  // overridden on the page
        Assert.Equal(300, second.MediaBox.Height, 3);
        Assert.Equal(90, second.Rotation);
    }

    private static byte[] BuildTwoPagePdf()
    {
        var ms = new MemoryStream();
        void W(string s) { byte[] b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }

        W("%PDF-1.4\n");
        long o1 = ms.Position; W("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        long o2 = ms.Position; W("2 0 obj\n<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 /MediaBox [0 0 612 792] /Resources << /Font << >> >> >>\nendobj\n");
        long o3 = ms.Position; W("3 0 obj\n<< /Type /Page /Parent 2 0 R >>\nendobj\n");
        long o4 = ms.Position; W("4 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 300] /Rotate 90 >>\nendobj\n");

        long xref = ms.Position;
        W("xref\n0 5\n");
        W("0000000000 65535 f \n");
        W($"{o1:D10} 00000 n \n");
        W($"{o2:D10} 00000 n \n");
        W($"{o3:D10} 00000 n \n");
        W($"{o4:D10} 00000 n \n");
        W("trailer\n<< /Size 5 /Root 1 0 R >>\n");
        W($"startxref\n{xref}\n%%EOF");
        return ms.ToArray();
    }
}
