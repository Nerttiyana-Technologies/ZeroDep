using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;
using ZeroDep.Filters;

namespace ZeroDep.Filters.Tests;

public sealed class FlateDecodeTests
{
    private static byte[] DeflateRaw(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }
        return ms.ToArray();
    }

    private static byte[] ZlibWrap(byte[] rawDeflate)
    {
        var wrapped = new byte[rawDeflate.Length + 2];
        wrapped[0] = 0x78; // CMF: DEFLATE, 32K window
        wrapped[1] = 0x9C; // FLG: default; 0x789C % 31 == 0
        Array.Copy(rawDeflate, 0, wrapped, 2, rawDeflate.Length);
        return wrapped;
    }

    [Fact]
    public void DecodesZlibWrappedData()
    {
        byte[] original = Encoding.ASCII.GetBytes("The quick brown fox jumps over ZeroDep.");
        byte[] encoded = ZlibWrap(DeflateRaw(original));
        Assert.Equal(original, FlateDecode.Decode(encoded));
    }

    [Fact]
    public void HasZlibHeaderDetectsStandardHeader()
    {
        Assert.True(FlateDecode.HasZlibHeader(new byte[] { 0x78, 0x9C, 0x00 }));
    }

    [Fact]
    public void HasZlibHeaderRejectsNonDeflateMethod()
    {
        Assert.False(FlateDecode.HasZlibHeader(new byte[] { 0x00, 0x01 }));
    }
}
