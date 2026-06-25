using System;

namespace ZeroDep.Filters.Jbig2;

/// <summary>
/// A JBIG2 bi-level bitmap: one byte per pixel, 1 = black (foreground), 0 = white (background),
/// row-major and top-to-bottom. Used for regions and the composited page.
/// </summary>
internal sealed class Jbig2Bitmap
{
    public Jbig2Bitmap(int width, int height, byte fill = 0)
    {
        Width = width < 0 ? 0 : width;
        Height = height < 0 ? 0 : height;
        Data = new byte[Width * Height];
        if (fill != 0)
        {
            for (int i = 0; i < Data.Length; i++)
            {
                Data[i] = fill;
            }
        }
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Data { get; }

    /// <summary>Reads a pixel, returning 0 for out-of-bounds positions.</summary>
    public int Get(int x, int y)
        => x >= 0 && x < Width && y >= 0 && y < Height ? Data[(y * Width) + x] : 0;

    public void Set(int x, int y, int value)
    {
        if (x >= 0 && x < Width && y >= 0 && y < Height)
        {
            Data[(y * Width) + x] = (byte)(value & 1);
        }
    }

    /// <summary>
    /// Composites another bitmap onto this one at (x, y) using a JBIG2 combination operator
    /// (0=OR, 1=AND, 2=XOR, 3=XNOR, 4=REPLACE).
    /// </summary>
    public void Combine(Jbig2Bitmap region, int x, int y, int op)
    {
        for (int ry = 0; ry < region.Height; ry++)
        {
            int py = y + ry;
            if (py < 0 || py >= Height)
            {
                continue;
            }

            for (int rx = 0; rx < region.Width; rx++)
            {
                int px = x + rx;
                if (px < 0 || px >= Width)
                {
                    continue;
                }

                int s = region.Data[(ry * region.Width) + rx];
                int idx = (py * Width) + px;
                int d = Data[idx];
                Data[idx] = (byte)(op switch
                {
                    0 => d | s,
                    1 => d & s,
                    2 => d ^ s,
                    3 => (d ^ s) ^ 1,
                    _ => s,
                });
            }
        }
    }
}
