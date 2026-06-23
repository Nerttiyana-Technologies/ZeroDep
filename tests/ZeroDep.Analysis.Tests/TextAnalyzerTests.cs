using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using ZeroDep.Abstractions;
using ZeroDep.Analysis;

namespace ZeroDep.Analysis.Tests;

public sealed class TextAnalyzerTests
{
    [Fact]
    public void ExtractsTextRunsAndPlainText()
    {
        byte[] pdf = BuildTextPdf("BT /F1 12 Tf 100 700 Td (Hello ZeroDep) Tj ET");

        var runs = TextAnalyzer.Analyze(new MemoryStream(pdf));
        TextRunInfo run = Assert.Single(runs);
        Assert.Equal("Hello ZeroDep", run.Text);
        Assert.Equal(0, run.PageIndex);

        string plain = TextAnalyzer.GetPlainText(new MemoryStream(pdf));
        Assert.Contains("Hello ZeroDep", plain);
    }

    private static byte[] BuildTextPdf(string content)
    {
        var ms = new MemoryStream();
        void W(string s) { byte[] b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }

        W("%PDF-1.4\n");
        long o1 = ms.Position; W("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        long o2 = ms.Position; W("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] >>\nendobj\n");
        long o3 = ms.Position; W("3 0 obj\n<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>\nendobj\n");
        long o4 = ms.Position; W($"4 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");
        long o5 = ms.Position; W("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        long xref = ms.Position;
        W("xref\n0 6\n0000000000 65535 f \n");
        foreach (long o in new[] { o1, o2, o3, o4, o5 }) W($"{o:D10} 00000 n \n");
        W("trailer\n<< /Size 6 /Root 1 0 R >>\n");
        W($"startxref\n{xref}\n%%EOF");
        return ms.ToArray();
    }
}
