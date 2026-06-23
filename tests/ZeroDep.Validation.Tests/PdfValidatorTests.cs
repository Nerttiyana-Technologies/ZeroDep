using System.IO;
using System.Text;
using Xunit;
using ZeroDep.Abstractions;
using ZeroDep.Validation;

namespace ZeroDep.Validation.Tests;

public sealed class PdfValidatorTests
{
    private static MemoryStream Bytes(string s) => new MemoryStream(Encoding.ASCII.GetBytes(s));

    [Fact]
    public void RejectsMissingHeader()
        => Assert.Equal(RejectionReason.MissingHeader, PdfValidator.Preflight(Bytes("definitely not a pdf file body in here")));

    [Fact]
    public void RejectsMissingEof()
        => Assert.Equal(RejectionReason.MissingEof, PdfValidator.Preflight(Bytes("%PDF-1.7\n1 0 obj<<>>endobj\n(no end marker)")));

    [Fact]
    public void AcceptsWellFormedMarkers()
        => Assert.Null(PdfValidator.Preflight(Bytes("%PDF-1.7\nbody\nstartxref\n9\n%%EOF")));

    [Theory]
    [InlineData("Missing 'startxref'.", RejectionReason.XrefUnresolvable)]
    [InlineData("Document catalog (/Root) was not found.", RejectionReason.CatalogUnreachable)]
    [InlineData("Stream ends before endstream.", RejectionReason.TruncatedStream)]
    [InlineData("Unterminated dictionary.", RejectionReason.MalformedObject)]
    [InlineData("Encrypted document could not be decrypted; a password is required (authentication failed).", RejectionReason.EncryptedPasswordRequired)]
    [InlineData("Unsupported public-key encryption handler.", RejectionReason.EncryptionUnsupported)]
    public void ClassifiesParseFailures(string message, RejectionReason expected)
        => Assert.Equal(expected, PdfValidator.Classify(message));
}
