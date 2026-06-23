using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using ZeroDep.Abstractions;
using ZeroDep.Analysis;

namespace ZeroDep.Analysis.Tests;

/// <summary>
/// Feature C (M5) edge-case coverage derived from the SF-1449 lessons: checkbox/radio state read
/// from the widget appearance state and field value (never text), name-based binding (never
/// geometry), fully-qualified names, abstention over fabrication, and flattened-form handling.
/// All fixtures target ISO 32000-2 §12.7 mechanisms; none copy any third-party code or parameters.
/// </summary>
public sealed class AcroFormEdgeCaseTests
{
    // C1: checkbox state lives in the widget /AS and field /V, validated against /AP /N keys.
    [Fact]
    public void Checkbox_State_ComesFromAppearanceState_NotText()
    {
        byte[] pdf = Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] >>",
            "<< /Type /Page /Parent 2 0 R /Annots [5 0 R 6 0 R 7 0 R] >>",
            "<< /Fields [5 0 R 6 0 R 7 0 R] >>",
            "<< /FT /Btn /T (accept) /V /Yes /AS /Yes /Subtype /Widget /Rect [0 0 10 10] /AP << /N << /Yes << >> /Off << >> >> >> >>",
            "<< /FT /Btn /T (decline) /AS /Off /Subtype /Widget /Rect [0 12 10 22] /AP << /N << /Yes << >> /Off << >> >> >> >>",
            "<< /FT /Btn /T (custom) /V /On /AS /On /Subtype /Widget /Rect [0 24 10 34] /AP << /N << /On << >> /Off << >> >> >> >>");

        AcroFormReport report = AcroFormAnalyzer.Analyze(new MemoryStream(pdf));

        Assert.True(Field(report, "accept").IsChecked);
        Assert.False(Field(report, "decline").IsChecked);

        FormFieldInfo custom = Field(report, "custom");
        Assert.True(custom.IsChecked);          // non-"Off" on-state name is honored
        Assert.Equal("On", custom.Value);       // the actual on-state name, not "Yes"
    }

    // C2: two adjacent checkboxes at the SAME geometry must each bind to their own field name.
    [Fact]
    public void Checkboxes_BindByFieldName_NotGeometry()
    {
        byte[] pdf = Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] >>",
            "<< /Type /Page /Parent 2 0 R /Annots [5 0 R 6 0 R] >>",
            "<< /Fields [5 0 R 6 0 R] >>",
            "<< /FT /Btn /T (are) /V /Yes /AS /Yes /Subtype /Widget /Rect [100 100 120 120] >>",
            "<< /FT /Btn /T (are_not) /AS /Off /Subtype /Widget /Rect [100 100 120 120] >>");

        AcroFormReport report = AcroFormAnalyzer.Analyze(new MemoryStream(pdf));

        Assert.True(Field(report, "are").IsChecked);
        Assert.False(Field(report, "are_not").IsChecked);
    }

    // C3: fully-qualified name joined down the /Parent chain, with /TU label and /V value.
    [Fact]
    public void FullyQualifiedName_AndLabel_AreResolved()
    {
        byte[] pdf = Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] >>",
            "<< /Type /Page /Parent 2 0 R /Annots [6 0 R] >>",
            "<< /Fields [5 0 R] >>",
            "<< /T (applicant) /Kids [6 0 R] >>",
            "<< /FT /Tx /T (name) /TU (Full Name) /V (Jane Roe) /Parent 5 0 R /Subtype /Widget /Rect [0 0 10 10] >>");

        AcroFormReport report = AcroFormAnalyzer.Analyze(new MemoryStream(pdf));

        FormFieldInfo field = Field(report, "applicant.name");
        Assert.Equal("Tx", field.FieldType);
        Assert.Equal("Full Name", field.Label);
        Assert.Equal("Jane Roe", field.Value);
        Assert.Equal(0, field.PageIndex);
    }

    // C4: a radio group selects one kid via the parent /V; the selected option is reported.
    [Fact]
    public void RadioGroup_SelectedOption_IsReported()
    {
        byte[] pdf = Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] >>",
            "<< /Type /Page /Parent 2 0 R /Annots [6 0 R 7 0 R] >>",
            "<< /Fields [5 0 R] >>",
            "<< /FT /Btn /T (color) /V /Green /Kids [6 0 R 7 0 R] >>",
            "<< /Subtype /Widget /Parent 5 0 R /AS /Red /Rect [0 0 10 10] /AP << /N << /Red << >> /Off << >> >> >> >>",
            "<< /Subtype /Widget /Parent 5 0 R /AS /Green /Rect [0 12 10 22] /AP << /N << /Green << >> /Off << >> >> >> >>");

        AcroFormReport report = AcroFormAnalyzer.Analyze(new MemoryStream(pdf));

        FormFieldInfo color = Field(report, "color");
        Assert.Equal("Btn", color.FieldType);
        Assert.True(color.IsChecked);
        Assert.Equal("Green", color.Value);     // the selected option, not a guess from geometry
        Assert.Equal(0, color.PageIndex);
    }

    // C5: an unresolvable button (no /V, no /AS) is reported unset — never fabricated as checked.
    [Fact]
    public void UnresolvableButton_IsReportedUnset_NotFabricated()
    {
        byte[] pdf = Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] >>",
            "<< /Type /Page /Parent 2 0 R /Annots [5 0 R] >>",
            "<< /Fields [5 0 R] >>",
            "<< /FT /Btn /T (maybe) /Subtype /Widget /Rect [0 0 10 10] >>");

        AcroFormReport report = AcroFormAnalyzer.Analyze(new MemoryStream(pdf));

        FormFieldInfo field = Field(report, "maybe");
        Assert.False(field.IsChecked);
        Assert.Null(field.Value);
    }

    // C7: a document with no /AcroForm yields no fields — the engine abstains, never invents.
    [Fact]
    public void NoAcroForm_AbstainsInsteadOfInventing()
    {
        byte[] pdf = Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] >>",
            "<< /Type /Page /Parent 2 0 R >>");

        AcroFormReport report = AcroFormAnalyzer.Analyze(new MemoryStream(pdf));

        Assert.False(report.HasAcroForm);
        Assert.Empty(report.Fields);
    }

    private static FormFieldInfo Field(AcroFormReport report, string fqn)
        => report.Fields.Single(f => f.FullyQualifiedName == fqn);

    /// <summary>Assembles a minimal PDF from object bodies; object 1 is the catalog.</summary>
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
