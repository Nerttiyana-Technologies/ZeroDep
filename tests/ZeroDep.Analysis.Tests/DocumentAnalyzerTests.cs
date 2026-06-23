using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using ZeroDep.Abstractions;
using ZeroDep.Analysis;

namespace ZeroDep.Analysis.Tests;

public sealed class DocumentAnalyzerTests
{
    [Fact]
    public void AssemblesUnifiedAnalysisWithCoverageManifest()
    {
        byte[] pdf = BuildTextPdf("BT /F1 12 Tf 100 700 Td (Hello ZeroDep) Tj ET");

        DocumentAnalysis analysis = DocumentAnalyzer.Analyze(new MemoryStream(pdf), AnalyzerOptions.DefaultDpiThreshold);

        Assert.Equal(1, analysis.PageCount);
        Assert.Contains(analysis.TextRuns, r => r.Text == "Hello ZeroDep");
        Assert.Contains(analysis.Coverage, c => c.Kind == "text" && c.Value == "Hello ZeroDep" && c.Page == 0);
        Assert.All(analysis.Coverage, c => Assert.False(string.IsNullOrEmpty(c.Id)));
    }


    [Fact]
    public void RejectsHeaderlessDocument()
    {
        var stream = new MemoryStream(Encoding.ASCII.GetBytes("this is not a pdf, no header anywhere in here"));
        DocumentAnalysis analysis = DocumentAnalyzer.Analyze(stream, AnalyzerOptions.DefaultDpiThreshold);

        Assert.Equal(DocumentStatus.Rejected, analysis.Status);
        Assert.NotNull(analysis.Rejection);
        Assert.Equal(RejectionReason.MissingHeader, analysis.Rejection!.Reason);
    }

    [Fact]
    public void ProcessesValidDocument()
    {
        byte[] pdf = BuildTextPdf("BT /F1 12 Tf 100 700 Td (Hi) Tj ET");
        DocumentAnalysis analysis = DocumentAnalyzer.Analyze(new MemoryStream(pdf), AnalyzerOptions.DefaultDpiThreshold);

        Assert.Equal(DocumentStatus.Processed, analysis.Status);
        Assert.Null(analysis.Rejection);
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
