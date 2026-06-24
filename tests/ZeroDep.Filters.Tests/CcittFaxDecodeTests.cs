using System;
using System.Linq;
using System.Text;
using Xunit;
using ZeroDep.Filters;

namespace ZeroDep.Filters.Tests;

/// <summary>
/// Validates the pure-BCL CCITT Group 4 decoder against a known fixture: a 16×6 bi-level bitmap
/// encoded as ITU-T T.6 (generated with libtiff, the same Group-4 coding PDFs use).
/// </summary>
public sealed class CcittFaxDecodeTests
{
    // The 16×6 source pattern ('1' = black, '.' = white), row-major.
    private const string Pattern =
        "1111....1111...." +
        "1.1.1.1.1.1.1.1." +
        "....1111....1111" +
        "11111111........" +
        "........11111111" +
        "1..............1";

    // The Group-4 codestream for the pattern above (libtiff, single strip, PDF-convention polarity).
    private static readonly byte[] G4 = FromHex(
        "26acdbc111d11d0450e088e88e825118b18b26a29986e66293547440020020");

    // The Group-3 one-dimensional (Modified Huffman, with EOLs) codestream for the same pattern.
    private static readonly byte[] G3 = FromHex(
        "0013576ec004d50e8743a1d0e8743a1c006ddb00135166003314004d5688");

    [Fact]
    public void Decode_Group4_ProducesExpectedBitmap()
    {
        var parms = new CcittParams { K = -1, Columns = 16, Rows = 6, BlackIs1 = false };

        RasterImage img = CcittFaxDecode.Decode(G4, parms);

        Assert.Equal(16, img.Width);
        Assert.Equal(6, img.Height);
        Assert.Equal(1, img.Components);

        // '1' (black) → sample 0, '.' (white) → sample 255.
        byte[] expected = Pattern.Select(c => c == '1' ? (byte)0 : (byte)255).ToArray();
        Assert.Equal(expected, img.Samples);
    }

    [Fact]
    public void Decode_Group4_BlackIs1_InvertsPolarity()
    {
        var parms = new CcittParams { K = -1, Columns = 16, Rows = 6, BlackIs1 = true };

        RasterImage img = CcittFaxDecode.Decode(G4, parms);

        byte[] expected = Pattern.Select(c => c == '1' ? (byte)255 : (byte)0).ToArray();
        Assert.Equal(expected, img.Samples);
    }

    [Fact]
    public void Decode_Group3OneDimensional_ProducesExpectedBitmap()
    {
        var parms = new CcittParams { K = 0, Columns = 16, Rows = 6, BlackIs1 = false };

        RasterImage img = CcittFaxDecode.Decode(G3, parms);

        Assert.Equal(16, img.Width);
        Assert.Equal(6, img.Height);

        byte[] expected = Pattern.Select(c => c == '1' ? (byte)0 : (byte)255).ToArray();
        Assert.Equal(expected, img.Samples);
    }

    private static byte[] FromHex(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        return bytes;
    }
}
