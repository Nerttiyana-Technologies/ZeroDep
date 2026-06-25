using Xunit;
using ZeroDep.Filters.Jbig2;

namespace ZeroDep.Filters.Tests;

/// <summary>
/// Validates the MQ arithmetic decoder against the canonical ITU-T T.88 Annex H.2 test sequence (the
/// same 32-byte stream used by jbig2dec). Decoding it with a single adaptive context must reproduce
/// the reference output. The distinctive 23-byte prefix (<c>00 02 00 51 … FC D7 9E</c>, including the
/// <c>2A AA AA AA AA</c> run) is the published reference; the tables are verified against T.88 Table
/// E.1 and the algorithm against the reference decoder. The generic-region corpus run is a second,
/// independent check (a broken MQ decoder would yield garbage bitmaps).
/// </summary>
public sealed class MqDecoderTests
{
    // The encoded test sequence (T.88 H.2 / jbig2dec), including its two trailing 0x00 bytes.
    private static readonly byte[] Encoded =
    {
        0x84, 0xC7, 0x3B, 0xFC, 0xE1, 0xA1, 0x43, 0x04, 0x02, 0x20,
        0x00, 0x00, 0x41, 0x0D, 0xBB, 0x86, 0xF4, 0x31, 0x7F, 0xFF,
        0x88, 0xFF, 0x37, 0x47, 0x1A, 0xDB, 0x6A, 0xDF, 0xFF, 0xAC,
        0x00, 0x00,
    };

    // The 256-bit decoded reference output.
    private static readonly byte[] Expected =
    {
        0x00, 0x02, 0x00, 0x51, 0x00, 0x00, 0x00, 0xC0, 0x03, 0x52,
        0x87, 0x2A, 0xAA, 0xAA, 0xAA, 0xAA, 0x82, 0xC0, 0x20, 0x00,
        0xFC, 0xD7, 0x9E, 0xF6, 0xBF, 0x7F, 0xED, 0x90, 0x4F, 0x46,
        0xA3, 0xBF,
    };

    [Fact]
    public void Decode_IsoTestSequence_ReproducesReference()
    {
        var mq = new MqDecoder(Encoded);
        var cx = new ArithContext(1);
        var output = new byte[32];

        for (int i = 0; i < 256; i++)
        {
            if (mq.Decode(cx, 0) != 0)
            {
                output[i >> 3] |= (byte)(0x80 >> (i & 7));
            }
        }

        Assert.Equal(Expected, output);
    }
}
