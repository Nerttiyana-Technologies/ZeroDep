using System.IO;
using System.Text;
using Xunit;
using ZeroDep.Abstractions;
using ZeroDep.Analysis;

namespace ZeroDep.Analysis.Tests;

/// <summary>
/// Feature B (M4) / B4: a dynamic XFA form (an <c>/AcroForm</c> carrying an <c>/XFA</c> packet) is
/// detected and flagged, so a consumer knows the visible page text is a placeholder and the real
/// content lives in the Adobe-only XFA stream. ISO 32000-2 §12.7.8.
/// </summary>
public sealed class XfaDetectionTests
{
    [Fact]
    public void DynamicXfaForm_IsFlagged()
    {
        byte[] pdf = Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] >>",
            "<< /Type /Page /Parent 2 0 R /Annots [5 0 R] >>",
            "<< /Fields [5 0 R] /XFA 6 0 R >>",
            "<< /FT /Tx /T (f1) /V (x) /Subtype /Widget /Rect [0 0 10 10] >>",
            "<< /Length 5 >>\nstream\nhello\nendstream");

        AcroFormReport report = AcroFormAnalyzer.Analyze(new MemoryStream(pdf));

        Assert.True(report.HasAcroForm);
        Assert.True(report.HasXfa);
    }

    [Fact]
    public void StaticAcroForm_IsNotFlaggedAsXfa()
    {
        byte[] pdf = Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] >>",
            "<< /Type /Page /Parent 2 0 R /Annots [5 0 R] >>",
            "<< /Fields [5 0 R] >>",
            "<< /FT /Tx /T (f1) /V (x) /Subtype /Widget /Rect [0 0 10 10] >>");

        AcroFormReport report = AcroFormAnalyzer.Analyze(new MemoryStream(pdf));

        Assert.True(report.HasAcroForm);
        Assert.False(report.HasXfa);
    }

    private static byte[] Build(params string[] objects)
    {
        var ms = new MemoryStream();
        void W(string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s);
            ms.Write(b, 0, b.Length);
        }

        W("%PDF-1.7\n");
        var offsets = new long[objects.Length];
        for (int i = 0; i < objects.Length; i++)
        {
            offsets[i] = ms.Position;
            W($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        long xref = ms.Position;
        W($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (long o in offsets)
        {
            W($"{o:D10} 00000 n \n");
        }

        W($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        return ms.ToArray();
    }
}
