using ZeroDep.Ocr;

namespace ZeroDep.Ocr.Paddle;

/// <summary>
/// Encodes a <see cref="DecodedImage"/> as a 24-bit BMP (pure BCL). The decoded samples are wrapped in
/// a minimal BMP container so PaddleOCR (which reads image files) can consume them.
/// </summary>
internal static class Bmp
{
    public static byte[] Encode(DecodedImage image)
    {
        int w = image.Width;
        int h = image.Height;
        int rowSize = (((w * 3) + 3) / 4) * 4;          // rows padded to a 4-byte boundary
        int dataSize = rowSize * h;
        int fileSize = 54 + dataSize;

        var bmp = new byte[fileSize];

        // BITMAPFILEHEADER (14 bytes)
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        WriteInt32(bmp, 2, fileSize);
        WriteInt32(bmp, 10, 54);                        // pixel-data offset

        // BITMAPINFOHEADER (40 bytes)
        WriteInt32(bmp, 14, 40);
        WriteInt32(bmp, 18, w);
        WriteInt32(bmp, 22, h);
        bmp[26] = 1;                                     // planes
        bmp[28] = 24;                                    // bits per pixel
        WriteInt32(bmp, 34, dataSize);

        byte[] src = image.Pixels;
        bool gray = image.Format == PixelFormat.Gray8;
        int channels = gray ? 1 : 3;

        for (int y = 0; y < h; y++)
        {
            int dstRow = 54 + ((h - 1 - y) * rowSize);   // BMP rows are bottom-to-top
            for (int x = 0; x < w; x++)
            {
                int si = ((y * w) + x) * channels;
                byte r, g, b;
                if (gray)
                {
                    r = g = b = si < src.Length ? src[si] : (byte)0;
                }
                else
                {
                    r = si < src.Length ? src[si] : (byte)0;
                    g = (si + 1) < src.Length ? src[si + 1] : (byte)0;
                    b = (si + 2) < src.Length ? src[si + 2] : (byte)0;
                }

                int di = dstRow + (x * 3);
                bmp[di] = b;                              // BMP stores BGR
                bmp[di + 1] = g;
                bmp[di + 2] = r;
            }
        }

        return bmp;
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }
}
