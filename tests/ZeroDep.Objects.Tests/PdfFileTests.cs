using System.IO;
using System.Text;
using Xunit;
using ZeroDep.Objects;

namespace ZeroDep.Objects.Tests;

public sealed class PdfFileTests
{
    [Fact]
    public void ResolvesClassicCrossReferenceTable()
    {
        byte[] pdf = BuildClassicPdf();
        using var file = PdfFile.Open(new MemoryStream(pdf));

        var catalog = Assert.IsType<PdfDictionary>(file.Resolve(file.Trailer["Root"]!));
        Assert.Equal("Catalog", Name(catalog["Type"]));

        var pages = Assert.IsType<PdfDictionary>(file.Resolve(catalog["Pages"]!));
        Assert.Equal(1L, Int(pages["Count"]));

        var kids = Assert.IsType<PdfArray>(pages["Kids"]!);
        var page = Assert.IsType<PdfDictionary>(file.Resolve(kids[0]));
        Assert.Equal("Page", Name(page["Type"]));
    }

    [Fact]
    public void ResolvesCrossReferenceStream()
    {
        byte[] pdf = BuildXrefStreamPdf();
        using var file = PdfFile.Open(new MemoryStream(pdf));

        var catalog = Assert.IsType<PdfDictionary>(file.Resolve(file.Trailer["Root"]!));
        Assert.Equal("Catalog", Name(catalog["Type"]));

        var page = Assert.IsType<PdfDictionary>(file.GetObject(3));
        Assert.Equal("Page", Name(page["Type"]));
    }

    [Fact]
    public void ResolvesObjectsInsideObjectStream()
    {
        byte[] pdf = BuildObjectStreamPdf();
        using var file = PdfFile.Open(new MemoryStream(pdf));

        var catalog = Assert.IsType<PdfDictionary>(file.GetObject(1));   // compressed in ObjStm 5
        Assert.Equal("Catalog", Name(catalog["Type"]));

        var pages = Assert.IsType<PdfDictionary>(file.GetObject(2));      // compressed in ObjStm 5
        Assert.Equal(1L, Int(pages["Count"]));

        var page = Assert.IsType<PdfDictionary>(file.GetObject(3));       // normal in-use object
        Assert.Equal("Page", Name(page["Type"]));
    }

    private static string Name(PdfObject? o) => Assert.IsType<PdfName>(o!).Value;

    private static long Int(PdfObject? o) => Assert.IsType<PdfInteger>(o!).Value;

    // ---- fixture builders ----

    private static byte[] BuildClassicPdf()
    {
        var ms = new MemoryStream();
        void W(string s) { byte[] b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }

        W("%PDF-1.4\n");
        long o1 = ms.Position; W("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        long o2 = ms.Position; W("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        long o3 = ms.Position; W("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");

        long xref = ms.Position;
        W("xref\n0 4\n");
        W("0000000000 65535 f \n");
        W($"{o1:D10} 00000 n \n");
        W($"{o2:D10} 00000 n \n");
        W($"{o3:D10} 00000 n \n");
        W("trailer\n<< /Size 4 /Root 1 0 R >>\n");
        W($"startxref\n{xref}\n%%EOF");
        return ms.ToArray();
    }

    private static byte[] BuildXrefStreamPdf()
    {
        var ms = new MemoryStream();
        void W(string s) { byte[] b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }

        W("%PDF-1.5\n");
        long o1 = ms.Position; W("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        long o2 = ms.Position; W("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        long o3 = ms.Position; W("3 0 obj\n<< /Type /Page /Parent 2 0 R >>\nendobj\n");
        long o4 = ms.Position;

        // /W [1 4 2] -> 7-byte records for objects 0..4
        byte[] data = new byte[5 * 7];
        void Rec(int i, long type, long f2, long f3)
        {
            int p = i * 7;
            data[p] = (byte)type;
            data[p + 1] = (byte)((f2 >> 24) & 0xFF);
            data[p + 2] = (byte)((f2 >> 16) & 0xFF);
            data[p + 3] = (byte)((f2 >> 8) & 0xFF);
            data[p + 4] = (byte)(f2 & 0xFF);
            data[p + 5] = (byte)((f3 >> 8) & 0xFF);
            data[p + 6] = (byte)(f3 & 0xFF);
        }
        Rec(0, 0, 0, 65535);
        Rec(1, 1, o1, 0);
        Rec(2, 1, o2, 0);
        Rec(3, 1, o3, 0);
        Rec(4, 1, o4, 0);

        W($"4 0 obj\n<< /Type /XRef /Size 5 /Root 1 0 R /W [1 4 2] /Index [0 5] /Length {data.Length} >>\nstream\n");
        ms.Write(data, 0, data.Length);
        W("\nendstream\nendobj\n");
        W($"startxref\n{o4}\n%%EOF");
        return ms.ToArray();
    }

    private static byte[] BuildObjectStreamPdf()
    {
        var ms = new MemoryStream();
        void W(string s) { byte[] b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }

        W("%PDF-1.5\n");
        long o3 = ms.Position; W("3 0 obj\n<< /Type /Page /Parent 2 0 R >>\nendobj\n");

        // Object stream (object 5) holding objects 1 and 2.
        string body1 = "<< /Type /Catalog /Pages 2 0 R >>";
        string body2 = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";
        int off2 = body1.Length + 1;                       // body2 starts after body1 + one space
        string header = $"1 0 2 {off2} ";
        int first = header.Length;
        byte[] objStmData = Encoding.ASCII.GetBytes(header + body1 + " " + body2);

        long o5 = ms.Position;
        W($"5 0 obj\n<< /Type /ObjStm /N 2 /First {first} /Length {objStmData.Length} >>\nstream\n");
        ms.Write(objStmData, 0, objStmData.Length);
        W("\nendstream\nendobj\n");

        // Cross-reference stream (object 6), /W [1 2 2] -> 5-byte records for objects 0..6.
        long o6 = ms.Position;
        byte[] data = new byte[7 * 5];
        void Rec(int i, long type, long f2, long f3)
        {
            int p = i * 5;
            data[p] = (byte)type;
            data[p + 1] = (byte)((f2 >> 8) & 0xFF);
            data[p + 2] = (byte)(f2 & 0xFF);
            data[p + 3] = (byte)((f3 >> 8) & 0xFF);
            data[p + 4] = (byte)(f3 & 0xFF);
        }
        Rec(0, 0, 0, 65535);
        Rec(1, 2, 5, 0);     // compressed: in ObjStm 5, index 0
        Rec(2, 2, 5, 1);     // compressed: in ObjStm 5, index 1
        Rec(3, 1, o3, 0);
        Rec(4, 0, 0, 0);
        Rec(5, 1, o5, 0);
        Rec(6, 1, o6, 0);

        W($"6 0 obj\n<< /Type /XRef /Size 7 /Root 1 0 R /W [1 2 2] /Index [0 7] /Length {data.Length} >>\nstream\n");
        ms.Write(data, 0, data.Length);
        W("\nendstream\nendobj\n");
        W($"startxref\n{o6}\n%%EOF");
        return ms.ToArray();
    }
}
