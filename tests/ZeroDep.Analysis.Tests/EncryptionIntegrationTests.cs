using System;
using System.IO;
using System.Linq;
using Xunit;
using ZeroDep.Abstractions;
using ZeroDep.Analysis;

namespace ZeroDep.Analysis.Tests;

// Synthetic encrypted fixtures (1-page PDF showing "ZeroDepSecret"), user password "u",
// generated with pikepdf. Decryption is proven by recovering the marker text.
public sealed class EncryptionIntegrationTests
{
    private const string Rc4Base64 = "JVBERi0xLjcKJb/3ov4KMSAwIG9iago8PCAvUGFnZXMgMiAwIFIgL1R5cGUgL0NhdGFsb2cgPj4KZW5kb2JqCjIgMCBvYmoKPDwgL0NvdW50IDEgL0tpZHMgWyAzIDAgUiBdIC9UeXBlIC9QYWdlcyA+PgplbmRvYmoKMyAwIG9iago8PCAvQ29udGVudHMgNCAwIFIgL01lZGlhQm94IDUgMCBSIC9QYXJlbnQgMiAwIFIgL1Jlc291cmNlcyA8PCAvRm9udCA8PCAvRjEgNiAwIFIgPj4gPj4gL1R5cGUgL1BhZ2UgPj4KZW5kb2JqCjQgMCBvYmoKPDwgL0xlbmd0aCA1MiAvRmlsdGVyIC9GbGF0ZURlY29kZSA+PgpzdHJlYW0Kgibx2L6JF11CKQUmEyLu9dr+tPFwhBnDlRQ+c7GvlOomaIxTWCQMJYdH7Wvu8fZnpLzsxwplbmRzdHJlYW0KZW5kb2JqCjUgMCBvYmoKWyAwIDAgMjAwIDIwMCBdCmVuZG9iago2IDAgb2JqCjw8IC9CYXNlRm9udCAvSGVsdmV0aWNhIC9TdWJ0eXBlIC9UeXBlMSAvVHlwZSAvRm9udCA+PgplbmRvYmoKNyAwIG9iago8PCAvQ0YgPDwgL1N0ZENGIDw8IC9BdXRoRXZlbnQgL0RvY09wZW4gL0NGTSAvVjIgL0xlbmd0aCAxNiA+PiA+PiAvRW5jcnlwdE1ldGFkYXRhIGZhbHNlIC9GaWx0ZXIgL1N0YW5kYXJkIC9MZW5ndGggMTI4IC9PIDwyYTJmMGExOTkwMTkyYzYwMTE0NzMwYmRjZDM5ZjM3ODI4YTUzYzg5YTM0MGRkNDczYzg1Mjk5ZGM1MjU4ZTFjPiAvT0UgPD4gL1AgLTEwMjggL1IgNCAvU3RtRiAvU3RkQ0YgL1N0ckYgL1N0ZENGIC9VIDw5MDNjMzkyMDcwZWFjMTRmYjcwNDQ3YjU1NGMxZjE0NjAwMjE0NDY5OTBiOWU0MTE0MDcxYTRkOTEwNDk4NGMxPiAvVUUgPD4gL1YgNCA+PgplbmRvYmoKeHJlZgowIDgKMDAwMDAwMDAwMCA2NTUzNSBmIAowMDAwMDAwMDE1IDAwMDAwIG4gCjAwMDAwMDAwNjQgMDAwMDAgbiAKMDAwMDAwMDEyMyAwMDAwMCBuIAowMDAwMDAwMjQxIDAwMDAwIG4gCjAwMDAwMDAzNjQgMDAwMDAgbiAKMDAwMDAwMDM5NSAwMDAwMCBuIAowMDAwMDAwNDY1IDAwMDAwIG4gCnRyYWlsZXIgPDwgL1Jvb3QgMSAwIFIgL1NpemUgOCAvSUQgWzw1Zjc3MDQ2OTRlZmM4MWMyNjg1M2RkMDc0MWM3MjE1MD48NWY3NzA0Njk0ZWZjODFjMjY4NTNkZDA3NDFjNzIxNTA+XSAvRW5jcnlwdCA3IDAgUiA+PgpzdGFydHhyZWYKODAxCiUlRU9GCg==";
    private const string Aes128Base64 = "JVBERi0xLjcKJb/3ov4KMSAwIG9iago8PCAvUGFnZXMgMiAwIFIgL1R5cGUgL0NhdGFsb2cgPj4KZW5kb2JqCjIgMCBvYmoKPDwgL0NvdW50IDEgL0tpZHMgWyAzIDAgUiBdIC9UeXBlIC9QYWdlcyA+PgplbmRvYmoKMyAwIG9iago8PCAvQ29udGVudHMgNCAwIFIgL01lZGlhQm94IDUgMCBSIC9QYXJlbnQgMiAwIFIgL1Jlc291cmNlcyA8PCAvRm9udCA8PCAvRjEgNiAwIFIgPj4gPj4gL1R5cGUgL1BhZ2UgPj4KZW5kb2JqCjQgMCBvYmoKPDwgL0xlbmd0aCA4MCAvRmlsdGVyIC9GbGF0ZURlY29kZSA+PgpzdHJlYW0KPxjGEzmVCM7fliiIDw6ZF5CliQtNcWaGkMLcSBj6ilXBAe7qQgj24TytgzJEV4kZqtoV98718sCI++q9b8FtiMSeh0gPmxTd77Q3TxsAJCcKZW5kc3RyZWFtCmVuZG9iago1IDAgb2JqClsgMCAwIDIwMCAyMDAgXQplbmRvYmoKNiAwIG9iago8PCAvQmFzZUZvbnQgL0hlbHZldGljYSAvU3VidHlwZSAvVHlwZTEgL1R5cGUgL0ZvbnQgPj4KZW5kb2JqCjcgMCBvYmoKPDwgL0NGIDw8IC9TdGRDRiA8PCAvQXV0aEV2ZW50IC9Eb2NPcGVuIC9DRk0gL0FFU1YyIC9MZW5ndGggMTYgPj4gPj4gL0ZpbHRlciAvU3RhbmRhcmQgL0xlbmd0aCAxMjggL08gPDJhMmYwYTE5OTAxOTJjNjAxMTQ3MzBiZGNkMzlmMzc4MjhhNTNjODlhMzQwZGQ0NzNjODUyOTlkYzUyNThlMWM+IC9PRSA8PiAvUCAtMTAyOCAvUiA0IC9TdG1GIC9TdGRDRiAvU3RyRiAvU3RkQ0YgL1UgPDdmNDBmZjQ3ZDA5ZDQ1NTI4ZjQyOWZkNjRiMTg4OGUzMDAyMTQ0Njk5MGI5ZTQxMTQwNzFhNGQ5MTA0OTg0YzE+IC9VRSA8PiAvViA0ID4+CmVuZG9iagp4cmVmCjAgOAowMDAwMDAwMDAwIDY1NTM1IGYgCjAwMDAwMDAwMTUgMDAwMDAgbiAKMDAwMDAwMDA2NCAwMDAwMCBuIAowMDAwMDAwMTIzIDAwMDAwIG4gCjAwMDAwMDAyNDEgMDAwMDAgbiAKMDAwMDAwMDM5MiAwMDAwMCBuIAowMDAwMDAwNDIzIDAwMDAwIG4gCjAwMDAwMDA0OTMgMDAwMDAgbiAKdHJhaWxlciA8PCAvUm9vdCAxIDAgUiAvU2l6ZSA4IC9JRCBbPDVmNzcwNDY5NGVmYzgxYzI2ODUzZGQwNzQxYzcyMTUwPjw1Zjc3MDQ2OTRlZmM4MWMyNjg1M2RkMDc0MWM3MjE1MD5dIC9FbmNyeXB0IDcgMCBSID4+CnN0YXJ0eHJlZgo4MDkKJSVFT0YK";
    private const string Aes256Base64 = "JVBERi0xLjcKJb/3ov4KMSAwIG9iago8PCAvRXh0ZW5zaW9ucyA8PCAvQURCRSA8PCAvQmFzZVZlcnNpb24gLzEuNyAvRXh0ZW5zaW9uTGV2ZWwgOCA+PiA+PiAvUGFnZXMgMiAwIFIgL1R5cGUgL0NhdGFsb2cgPj4KZW5kb2JqCjIgMCBvYmoKPDwgL0NvdW50IDEgL0tpZHMgWyAzIDAgUiBdIC9UeXBlIC9QYWdlcyA+PgplbmRvYmoKMyAwIG9iago8PCAvQ29udGVudHMgNCAwIFIgL01lZGlhQm94IDUgMCBSIC9QYXJlbnQgMiAwIFIgL1Jlc291cmNlcyA8PCAvRm9udCA8PCAvRjEgNiAwIFIgPj4gPj4gL1R5cGUgL1BhZ2UgPj4KZW5kb2JqCjQgMCBvYmoKPDwgL0xlbmd0aCA4MCAvRmlsdGVyIC9GbGF0ZURlY29kZSA+PgpzdHJlYW0K3iLoNPYIa7ted6y6LOQnB/n3DKV26ruTV3Tmb1ChFlcrpURdcK2dGd0+t87GJo3Y2lmhZZxOw/zWXS7X+okeMsGsEEGm/cAygN4zVXxtPM4KZW5kc3RyZWFtCmVuZG9iago1IDAgb2JqClsgMCAwIDIwMCAyMDAgXQplbmRvYmoKNiAwIG9iago8PCAvQmFzZUZvbnQgL0hlbHZldGljYSAvU3VidHlwZSAvVHlwZTEgL1R5cGUgL0ZvbnQgPj4KZW5kb2JqCjcgMCBvYmoKPDwgL0NGIDw8IC9TdGRDRiA8PCAvQXV0aEV2ZW50IC9Eb2NPcGVuIC9DRk0gL0FFU1YzIC9MZW5ndGggMzIgPj4gPj4gL0ZpbHRlciAvU3RhbmRhcmQgL0xlbmd0aCAyNTYgL08gPDhmYTI0YzdlNmFiZGRhYzc5ZjA3M2Q3Zjk1NjQ1MzBlZjE5NTNiZDc5YTY0NmViNzI4NDE1M2M1Y2I2ZTk5MWM1ODQxNWQ0YjQyNjI3MDBlNDJkOTY4MDg4MTBiNjM3ZD4gL09FIDw5Yzc2ODNhMGEzZDkxMzRkY2E0ODNmNzM1YzA2MjkyMmJhYzM2MTY1YjNiZDcyNWU1ZDAxZjU2NTI4ZjZkY2JmPiAvUCAtMTAyOCAvUGVybXMgPDQ4MGIzMWIyZWFlYTk3MTQ0ODk5ZmJlYmU3MzJmYjMzPiAvUiA2IC9TdG1GIC9TdGRDRiAvU3RyRiAvU3RkQ0YgL1UgPDg5N2YwYmZmNWRkZTcwZmYxMTg1MThlMTI2MDMxMjFlZjc4NWY0N2FiZTdkOTExMTJhYzNkMjBjMjE4YzdmMTY0MjhiODlkNDFkYmI0YWRjMjliOTUwNjJlZDhlODAzYT4gL1VFIDxkZmIwNmYwOGRjYjJkMTNkMTExZTlmMWEzYmZmNGY1Yzk3MzU3ZjJjMTk3ZjQ5NDM4ZTYwY2Y1YjIyMDI3Mjc2PiAvViA1ID4+CmVuZG9iagp4cmVmCjAgOAowMDAwMDAwMDAwIDY1NTM1IGYgCjAwMDAwMDAwMTUgMDAwMDAgbiAKMDAwMDAwMDEzMCAwMDAwMCBuIAowMDAwMDAwMTg5IDAwMDAwIG4gCjAwMDAwMDAzMDcgMDAwMDAgbiAKMDAwMDAwMDQ1OCAwMDAwMCBuIAowMDAwMDAwNDg5IDAwMDAwIG4gCjAwMDAwMDA1NTkgMDAwMDAgbiAKdHJhaWxlciA8PCAvUm9vdCAxIDAgUiAvU2l6ZSA4IC9JRCBbPDVmNzcwNDY5NGVmYzgxYzI2ODUzZGQwNzQxYzcyMTUwPjw1Zjc3MDQ2OTRlZmM4MWMyNjg1M2RkMDc0MWM3MjE1MD5dIC9FbmNyeXB0IDcgMCBSID4+CnN0YXJ0eHJlZgoxMTA5CiUlRU9GCg==";

    private static DocumentAnalysis Analyze(string base64, string? password)
        => DocumentAnalyzer.Analyze(new MemoryStream(Convert.FromBase64String(base64)), 150, password);

    private static bool HasMarker(DocumentAnalysis a)
        => a.Coverage.Any(c => c.Value.IndexOf("ZeroDepSecret", StringComparison.Ordinal) >= 0);

    [Theory]
    [InlineData(Rc4Base64, EncryptionAlgorithm.Rc4)]
    [InlineData(Aes128Base64, EncryptionAlgorithm.Aes128)]
    [InlineData(Aes256Base64, EncryptionAlgorithm.Aes256)]
    public void DecryptsWithUserPassword(string base64, EncryptionAlgorithm expected)
    {
        DocumentAnalysis a = Analyze(base64, "u");

        Assert.Equal(DocumentStatus.Processed, a.Status);
        Assert.True(a.Security.IsEncrypted);
        Assert.Equal(expected, a.Security.Algorithm);
        Assert.Equal(AuthenticationResult.UserPassword, a.Security.Authentication);
        Assert.True(HasMarker(a), "decrypted text should contain the marker");
    }

    [Fact]
    public void WrongPasswordIsRejectedCleanly()
    {
        // No crash: an encrypted document we cannot authenticate is rejected with a clear reason.
        DocumentAnalysis a = Analyze(Aes256Base64, "wrong-password");
        Assert.Equal(DocumentStatus.Rejected, a.Status);
        Assert.NotNull(a.Rejection);
        Assert.Equal(RejectionReason.EncryptedPasswordRequired, a.Rejection!.Reason);
        Assert.False(HasMarker(a), "wrong password must not recover the marker");
    }

    // E1: a password-protected document opened without the password fails cleanly (structured, no
    // throw) and never leaks content. (These fixtures use the user password "u".)
    [Theory]
    [InlineData(Rc4Base64)]
    [InlineData(Aes128Base64)]
    [InlineData(Aes256Base64)]
    public void PasswordProtectedDocument_WithoutPassword_RejectsCleanly(string base64)
    {
        DocumentAnalysis a = Analyze(base64, password: null);

        Assert.Equal(DocumentStatus.Rejected, a.Status);
        Assert.NotNull(a.Rejection);
        Assert.False(HasMarker(a), "an unauthenticated document must not leak content");
    }

    // E1 baseline: an unencrypted document requires no authentication.
    [Fact]
    public void UnencryptedDocument_RequiresNoAuthentication()
    {
        DocumentAnalysis a = DocumentAnalyzer.Analyze(new MemoryStream(BuildPlainPdf()), 150);

        Assert.Equal(DocumentStatus.Processed, a.Status);
        Assert.False(a.Security.IsEncrypted);
        Assert.Equal(AuthenticationResult.NotRequired, a.Security.Authentication);
        Assert.Equal(EncryptionAlgorithm.None, a.Security.Algorithm);
    }

    private static byte[] BuildPlainPdf()
    {
        const string content = "BT /F1 12 Tf 72 720 Td (Plain) Tj ET";
        var ms = new MemoryStream();
        void W(string s)
        {
            byte[] b = System.Text.Encoding.ASCII.GetBytes(s);
            ms.Write(b, 0, b.Length);
        }

        W("%PDF-1.4\n");
        long o1 = ms.Position; W("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        long o2 = ms.Position; W("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] >>\nendobj\n");
        long o3 = ms.Position; W("3 0 obj\n<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>\nendobj\n");
        long o4 = ms.Position; W($"4 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");
        long o5 = ms.Position; W("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        long xref = ms.Position;
        W("xref\n0 6\n0000000000 65535 f \n");
        foreach (long o in new[] { o1, o2, o3, o4, o5 })
        {
            W($"{o:D10} 00000 n \n");
        }

        W("trailer\n<< /Size 6 /Root 1 0 R >>\n");
        W($"startxref\n{xref}\n%%EOF");
        return ms.ToArray();
    }
}
