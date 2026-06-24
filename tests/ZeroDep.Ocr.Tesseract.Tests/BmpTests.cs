using Xunit;
using ZeroDep.Ocr;
using ZeroDep.Ocr.Tesseract;

namespace ZeroDep.Ocr.Tesseract.Tests;

/// <summary>
/// The BMP wrapper used to hand decoded pixels to Tesseract — pure BCL, no native libs required.
/// (The engine itself needs libtesseract + tessdata and is validated separately on a real machine.)
/// </summary>
public sealed class BmpTests
{
    [Fact]
    public void Encode_Rgb24_ProducesWellFormedBmp()
    {
        var image = new DecodedImage
        {
            Width = 2,
            Height = 2,
            Format = PixelFormat.Rgb24,
            Pixels = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120 },
        };

        byte[] bmp = Bmp.Encode(image);

        Assert.Equal((byte)'B', bmp[0]);
        Assert.Equal((byte)'M', bmp[1]);
        Assert.Equal(24, bmp[28]);                                  // bits per pixel
        Assert.Equal(2, ReadInt32(bmp, 18));                        // width
        Assert.Equal(2, ReadInt32(bmp, 22));                        // height
        Assert.Equal(54, ReadInt32(bmp, 10));                       // pixel-data offset

        // Row width 2*3 = 6, padded to 8; data = 8*2 = 16; file = 54 + 16 = 70.
        Assert.Equal(70, bmp.Length);

        // BMP is bottom-up: the image's top-left pixel (R=10,G=20,B=30) sits in the LAST stored row,
        // written as BGR.
        int lastRow = 54 + (1 * 8);
        Assert.Equal(30, bmp[lastRow]);       // B
        Assert.Equal(20, bmp[lastRow + 1]);   // G
        Assert.Equal(10, bmp[lastRow + 2]);   // R
    }

    [Fact]
    public void Encode_Gray8_ReplicatesChannel()
    {
        var image = new DecodedImage
        {
            Width = 1,
            Height = 1,
            Format = PixelFormat.Gray8,
            Pixels = new byte[] { 200 },
        };

        byte[] bmp = Bmp.Encode(image);
        int row = 54;
        Assert.Equal(200, bmp[row]);
        Assert.Equal(200, bmp[row + 1]);
        Assert.Equal(200, bmp[row + 2]);
    }

    private static int ReadInt32(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
}
