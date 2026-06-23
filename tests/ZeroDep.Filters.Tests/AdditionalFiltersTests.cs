using System.Text;
using Xunit;
using ZeroDep.Filters;

namespace ZeroDep.Filters.Tests;

public sealed class AdditionalFiltersTests
{
    [Fact]
    public void AsciiHexDecodes()
    {
        byte[] outp = AsciiHexDecode.Decode(Encoding.ASCII.GetBytes("48656C6C6F>"));
        Assert.Equal("Hello", Encoding.ASCII.GetString(outp));
    }

    [Fact]
    public void AsciiHexPadsOddTrailingNibble()
    {
        // "4" -> 0x40
        Assert.Equal(new byte[] { 0x40 }, AsciiHexDecode.Decode(Encoding.ASCII.GetBytes("4>")));
    }

    [Fact]
    public void Ascii85Decodes()
    {
        // Adobe ASCII85 of "Hello, ZeroDep!" (no <~ ~> framing)
        byte[] outp = Ascii85Decode.Decode(Encoding.ASCII.GetBytes("87cURD_*#7ATD]WAT/d"));
        Assert.Equal("Hello, ZeroDep!", Encoding.ASCII.GetString(outp));
    }

    [Fact]
    public void RunLengthDecodesLiteralAndRepeat()
    {
        // literal run of 5 ("Hello") then EOD
        Assert.Equal("Hello", Encoding.ASCII.GetString(
            RunLengthDecode.Decode(new byte[] { 4, 72, 101, 108, 108, 111, 128 })));
        // repeat: length 254 -> 257-254 = 3 copies of 'A'
        Assert.Equal("AAA", Encoding.ASCII.GetString(
            RunLengthDecode.Decode(new byte[] { 254, 65, 128 })));
    }

    [Fact]
    public void LzwDecodes()
    {
        byte[] outp = LzwDecode.Decode(new byte[] { 128, 11, 96, 80, 34, 12, 12, 133, 1 });
        Assert.Equal("-----A---B", Encoding.ASCII.GetString(outp));
    }
}
