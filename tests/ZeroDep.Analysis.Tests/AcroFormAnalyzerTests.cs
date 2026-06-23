using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using ZeroDep.Abstractions;
using ZeroDep.Analysis;

namespace ZeroDep.Analysis.Tests;

public sealed class AcroFormAnalyzerTests
{
    [Fact]
    public void ExtractsFieldsCheckboxStateAndHierarchy()
    {
        byte[] pdf = BuildAcroFormPdf();
        AcroFormReport report = AcroFormAnalyzer.Analyze(new MemoryStream(pdf));

        Assert.True(report.HasAcroForm);
        Assert.Equal(4, report.Fields.Count); // vendor, sole_source, set_aside, addr.city (parent 'addr' is intermediate)

        FormFieldInfo vendor = Field(report, "vendor");
        Assert.Equal("Tx", vendor.FieldType);
        Assert.Equal("Vendor Name", vendor.Label);
        Assert.Equal("Acme Corp", vendor.Value);
        Assert.Equal(0, vendor.PageIndex);
        Assert.Null(vendor.IsChecked);
        Assert.NotNull(vendor.Rect);
        Assert.Equal(10, vendor.Rect!.Value.Width, 3);
        Assert.Equal(10, vendor.Rect!.Value.Height, 3);

        FormFieldInfo sole = Field(report, "sole_source");
        Assert.Equal("Btn", sole.FieldType);
        Assert.True(sole.IsChecked == true);          // /V /Yes and /AS /Yes
        Assert.Equal(0, sole.PageIndex);

        FormFieldInfo setAside = Field(report, "set_aside");
        Assert.Equal("Btn", setAside.FieldType);
        Assert.False(setAside.IsChecked == true);     // /V /Off

        FormFieldInfo city = Field(report, "addr.city");
        Assert.Equal("Tx", city.FieldType);
        Assert.Equal("Reston", city.Value);   // fully-qualified name joined down the parent chain
    }

    private static FormFieldInfo Field(AcroFormReport report, string fqn)
        => report.Fields.Single(f => f.FullyQualifiedName == fqn);

    private static byte[] BuildAcroFormPdf()
    {
        var ms = new MemoryStream();
        void W(string s) { byte[] b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }

        W("%PDF-1.5\n");
        long o1 = ms.Position; W("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /AcroForm 4 0 R >>\nendobj\n");
        long o2 = ms.Position; W("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] >>\nendobj\n");
        long o3 = ms.Position; W("3 0 obj\n<< /Type /Page /Parent 2 0 R /Annots [5 0 R 6 0 R 7 0 R 9 0 R] >>\nendobj\n");
        long o4 = ms.Position; W("4 0 obj\n<< /Fields [5 0 R 6 0 R 7 0 R 8 0 R] >>\nendobj\n");
        long o5 = ms.Position; W("5 0 obj\n<< /FT /Tx /T (vendor) /TU (Vendor Name) /V (Acme Corp) /Subtype /Widget /Rect [0 0 10 10] >>\nendobj\n");
        long o6 = ms.Position; W("6 0 obj\n<< /FT /Btn /T (sole_source) /V /Yes /AS /Yes /Subtype /Widget /Rect [0 0 10 10] >>\nendobj\n");
        long o7 = ms.Position; W("7 0 obj\n<< /FT /Btn /T (set_aside) /V /Off /AS /Off /Subtype /Widget /Rect [0 0 10 10] >>\nendobj\n");
        long o8 = ms.Position; W("8 0 obj\n<< /T (addr) /Kids [9 0 R] >>\nendobj\n");
        long o9 = ms.Position; W("9 0 obj\n<< /FT /Tx /T (city) /V (Reston) /Parent 8 0 R /Subtype /Widget /Rect [0 0 10 10] >>\nendobj\n");

        long xref = ms.Position;
        W("xref\n0 10\n");
        W("0000000000 65535 f \n");
        foreach (long o in new[] { o1, o2, o3, o4, o5, o6, o7, o8, o9 })
        {
            W($"{o:D10} 00000 n \n");
        }
        W("trailer\n<< /Size 10 /Root 1 0 R >>\n");
        W($"startxref\n{xref}\n%%EOF");
        return ms.ToArray();
    }
}
