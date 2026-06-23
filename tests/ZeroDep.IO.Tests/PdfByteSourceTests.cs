using System.IO;
using System.Text;
using Xunit;
using ZeroDep.IO;

namespace ZeroDep.IO.Tests;

public sealed class PdfByteSourceTests
{
    private static PdfByteSource From(string text)
        => PdfByteSource.Create(new MemoryStream(Encoding.ASCII.GetBytes(text)));

    [Fact]
    public void ReadsBytesAtPosition()
    {
        using var src = From("%PDF-1.7 hello");
        byte[] read = src.ReadBytes(9, 5);
        Assert.Equal("hello", Encoding.ASCII.GetString(read));
    }

    [Fact]
    public void ReadByteAtReturnsMinusOneBeyondEnd()
    {
        using var src = From("abc");
        Assert.Equal((int)'a', src.ReadByteAt(0));
        Assert.Equal(-1, src.ReadByteAt(99));
    }

    [Fact]
    public void IndexOfFindsForwardPattern()
    {
        using var src = From("0000 startxref 1234");
        long pos = src.IndexOf(Encoding.ASCII.GetBytes("startxref"), 0);
        Assert.Equal(5L, pos);
    }

    [Fact]
    public void IndexOfReturnsMinusOneWhenAbsent()
    {
        using var src = From("no marker here");
        Assert.Equal(-1L, src.IndexOf(Encoding.ASCII.GetBytes("%%EOF"), 0));
    }

    [Fact]
    public void LastIndexOfFindsTrailingMarker()
    {
        using var src = From("xref ... trailer ... %%EOF\n");
        long pos = src.LastIndexOf(Encoding.ASCII.GetBytes("%%EOF"), src.Length);
        Assert.True(pos > 0);
        Assert.Equal("%%EOF", Encoding.ASCII.GetString(src.ReadBytes(pos, 5)));
    }

    [Fact]
    public void NonSeekableStreamIsBuffered()
    {
        using var inner = new NonSeekableStream(Encoding.ASCII.GetBytes("seekme"));
        using var src = PdfByteSource.Create(inner);
        Assert.Equal("seekme", Encoding.ASCII.GetString(src.ReadBytes(0, 6)));
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public NonSeekableStream(byte[] data) : base(data) { }
        public override bool CanSeek => false;
    }
}
