using System;
using System.Collections.Generic;
using System.IO;

namespace ZeroDep.Filters;

/// <summary>
/// A pure-BCL baseline / extended-sequential JPEG (<c>/DCTDecode</c>) decoder (ITU-T T.81). Decodes
/// 1-component (grayscale) and 3-component (YCbCr→RGB) images, including chroma subsampling and
/// restart intervals. Progressive, arithmetic, and 4-component (CMYK) JPEGs are not yet supported and
/// are reported via <see cref="NotSupportedException"/> for the caller to handle.
/// </summary>
public static partial class JpegDecoder
{
    private static readonly int[] ZigZag =
    {
        0, 1, 8, 16, 9, 2, 3, 10,
        17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    };

    // cos[u, x] = cos((2x+1) * u * pi / 16); cu[u] = u == 0 ? 1/sqrt2 : 1.
    private static readonly double[,] Cos = BuildCosLut();
    private static readonly double[] Cu = BuildCuLut();

    /// <summary>Decodes a baseline JPEG into a <see cref="RasterImage"/>.</summary>
    /// <param name="data">The raw JPEG bytes (a <c>/DCTDecode</c> image stream).</param>
    /// <returns>The decoded image (1 or 3 components).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="InvalidDataException">The JPEG is malformed.</exception>
    /// <exception cref="NotSupportedException">The JPEG uses an unsupported process or component count.</exception>
    public static RasterImage Decode(byte[] data)
    {
        PlaneSet planes = DecodeToPlanes(data);
        return Combine(planes.Meta, planes.Planes, planes.PlaneW, planes.HMax, planes.VMax);
    }

    internal static PlaneSet DecodeToPlanes(byte[] data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        JpegMetadata meta = JpegReader.ReadMetadata(data);
        if (meta.Mode == JpegMode.Unsupported)
        {
            throw new NotSupportedException("Unsupported JPEG coding process (lossless / arithmetic).");
        }

        if (meta.ComponentCount != 1 && meta.ComponentCount != 3 && meta.ComponentCount != 4)
        {
            throw new NotSupportedException("Only 1-, 3-, or 4-component JPEG is supported.");
        }

        if (meta.Mode == JpegMode.Progressive)
        {
            return DecodeProgressive(data, meta);
        }

        var huff = new Dictionary<int, HuffTable>();
        foreach (JpegHuffmanTable t in meta.HuffmanTables)
        {
            huff[(t.TableClass << 4) | t.Id] = HuffTable.Build(t.CodeLengthCounts, t.Symbols);
        }

        Scan scan = FindScan(data, meta);
        return DecodeBaseline(data, meta, huff, scan);
    }

    /// <summary>Decoded component planes before colour combination (exposed for diagnostics/tests).</summary>
    internal sealed class PlaneSet
    {
        public JpegMetadata Meta { get; set; } = new JpegMetadata();

        public byte[][] Planes { get; set; } = Array.Empty<byte[]>();

        public int[] PlaneW { get; set; } = Array.Empty<int>();

        public int HMax { get; set; }

        public int VMax { get; set; }
    }

    private sealed class Scan
    {
        public int EntropyStart { get; set; }

        public int[] DcTable { get; set; } = Array.Empty<int>();   // per frame-component index

        public int[] AcTable { get; set; } = Array.Empty<int>();
    }

    private static Scan FindScan(byte[] d, JpegMetadata meta)
    {
        int pos = 2;
        while (pos + 1 < d.Length)
        {
            if (d[pos] != 0xFF)
            {
                pos++;
                continue;
            }

            int marker = d[pos + 1];
            pos += 2;
            if (marker == 0xFF)
            {
                pos--;
                continue;
            }

            if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0x01)
            {
                continue;
            }

            if (pos + 2 > d.Length)
            {
                break;
            }

            int length = (d[pos] << 8) | d[pos + 1];
            int segStart = pos + 2;
            int segEnd = pos + length;
            if (marker == 0xDA)
            {
                int ns = d[segStart];
                var dc = new int[meta.ComponentCount];
                var ac = new int[meta.ComponentCount];
                int p = segStart + 1;
                for (int i = 0; i < ns; i++)
                {
                    int selector = d[p];
                    int tables = d[p + 1];
                    p += 2;
                    int frameIndex = IndexOfComponent(meta, selector);
                    if (frameIndex >= 0)
                    {
                        dc[frameIndex] = (tables >> 4) & 0x0F;
                        ac[frameIndex] = tables & 0x0F;
                    }
                }

                return new Scan { EntropyStart = segEnd, DcTable = dc, AcTable = ac };
            }

            pos = segEnd;
        }

        throw new InvalidDataException("JPEG has no scan (SOS).");
    }

    private static int IndexOfComponent(JpegMetadata meta, int id)
    {
        for (int i = 0; i < meta.Components.Count; i++)
        {
            if (meta.Components[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private static PlaneSet DecodeBaseline(byte[] data, JpegMetadata meta, Dictionary<int, HuffTable> huff, Scan scan)
    {
        int n = meta.ComponentCount;
        int hMax = 1, vMax = 1;
        foreach (JpegComponent c in meta.Components)
        {
            hMax = Math.Max(hMax, c.HorizontalSampling);
            vMax = Math.Max(vMax, c.VerticalSampling);
        }

        int mcusX = (meta.Width + (8 * hMax) - 1) / (8 * hMax);
        int mcusY = (meta.Height + (8 * vMax) - 1) / (8 * vMax);

        var planeW = new int[n];
        var planeH = new int[n];
        var planes = new byte[n][];
        var quant = new int[n][];
        for (int c = 0; c < n; c++)
        {
            JpegComponent comp = meta.Components[c];
            planeW[c] = mcusX * comp.HorizontalSampling * 8;
            planeH[c] = mcusY * comp.VerticalSampling * 8;
            planes[c] = new byte[planeW[c] * planeH[c]];
            quant[c] = meta.QuantizationTables.TryGetValue(comp.QuantizationTableId, out int[]? q) ? q : OnesQuant();
        }

        var reader = new BitReader(data, scan.EntropyStart);
        var pred = new int[n];
        var coef = new int[64];
        var block = new byte[64];
        int restart = meta.RestartInterval;
        int mcuCount = 0;

        for (int my = 0; my < mcusY; my++)
        {
            for (int mx = 0; mx < mcusX; mx++)
            {
                for (int c = 0; c < n; c++)
                {
                    JpegComponent comp = meta.Components[c];
                    HuffTable dc = huff[(0 << 4) | scan.DcTable[c]];
                    HuffTable ac = huff[(1 << 4) | scan.AcTable[c]];
                    for (int by = 0; by < comp.VerticalSampling; by++)
                    {
                        for (int bx = 0; bx < comp.HorizontalSampling; bx++)
                        {
                            DecodeBlock(reader, dc, ac, quant[c], coef, ref pred[c]);
                            InverseDct(coef, block);
                            int originX = ((mx * comp.HorizontalSampling) + bx) * 8;
                            int originY = ((my * comp.VerticalSampling) + by) * 8;
                            PlaceBlock(planes[c], planeW[c], originX, originY, block);
                        }
                    }
                }

                mcuCount++;
                if (restart > 0 && mcuCount % restart == 0 && !(my == mcusY - 1 && mx == mcusX - 1))
                {
                    reader.Restart();
                    Array.Clear(pred, 0, pred.Length);
                }
            }
        }

        return new PlaneSet { Meta = meta, Planes = planes, PlaneW = planeW, HMax = hMax, VMax = vMax };
    }

    private static void DecodeBlock(BitReader r, HuffTable dc, HuffTable ac, int[] quant, int[] coef, ref int pred)
    {
        Array.Clear(coef, 0, 64);

        int s = dc.Decode(r);
        int diff = s == 0 ? 0 : Extend(r.Receive(s), s);
        pred += diff;
        coef[0] = pred * quant[0];

        int k = 1;
        while (k < 64)
        {
            int rs = ac.Decode(r);
            int run = rs >> 4;
            int size = rs & 0x0F;
            if (size == 0)
            {
                if (run == 15)
                {
                    k += 16;       // ZRL: 16 zero coefficients
                    continue;
                }

                break;             // EOB
            }

            k += run;
            if (k > 63)
            {
                break;
            }

            int value = Extend(r.Receive(size), size);
            coef[ZigZag[k]] = value * quant[k];
            k++;
        }
    }

    private static void InverseDct(int[] coef, byte[] outBlock)
    {
        var tmp = new double[64];
        for (int row = 0; row < 8; row++)
        {
            for (int x = 0; x < 8; x++)
            {
                double sum = 0;
                for (int u = 0; u < 8; u++)
                {
                    sum += Cu[u] * coef[(row * 8) + u] * Cos[u, x];
                }

                tmp[(row * 8) + x] = sum;
            }
        }

        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                double sum = 0;
                for (int v = 0; v < 8; v++)
                {
                    sum += Cu[v] * tmp[(v * 8) + x] * Cos[v, y];
                }

                int sample = (int)Math.Round(sum * 0.25) + 128;
                outBlock[(y * 8) + x] = (byte)(sample < 0 ? 0 : sample > 255 ? 255 : sample);
            }
        }
    }

    private static void PlaceBlock(byte[] plane, int planeWidth, int originX, int originY, byte[] block)
    {
        for (int y = 0; y < 8; y++)
        {
            int dst = ((originY + y) * planeWidth) + originX;
            int src = y * 8;
            for (int x = 0; x < 8; x++)
            {
                plane[dst + x] = block[src + x];
            }
        }
    }

    private static RasterImage Combine(JpegMetadata meta, byte[][] planes, int[] planeW, int hMax, int vMax)
    {
        int w = meta.Width;
        int h = meta.Height;

        if (meta.ComponentCount == 1)
        {
            var gray = new byte[w * h];
            int pw = planeW[0];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    gray[(y * w) + x] = planes[0][(y * pw) + x];
                }
            }

            return new RasterImage { Width = w, Height = h, Components = 1, Samples = gray };
        }

        var rgb = new byte[w * h * 3];
        IReadOnlyList<JpegComponent> comps = meta.Components;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int o = ((y * w) + x) * 3;
                if (meta.ComponentCount == 3)
                {
                    double c0 = Sample(planes[0], planeW[0], x, y, comps[0], hMax, vMax);
                    double c1 = Sample(planes[1], planeW[1], x, y, comps[1], hMax, vMax);
                    double c2 = Sample(planes[2], planeW[2], x, y, comps[2], hMax, vMax);

                    if (meta.AdobeTransform == 0)
                    {
                        rgb[o] = Clamp((int)Math.Round(c0));        // stored directly as RGB
                        rgb[o + 1] = Clamp((int)Math.Round(c1));
                        rgb[o + 2] = Clamp((int)Math.Round(c2));
                    }
                    else
                    {
                        YccToRgb(c0, c1, c2, out int r, out int g, out int b);
                        rgb[o] = Clamp(r);
                        rgb[o + 1] = Clamp(g);
                        rgb[o + 2] = Clamp(b);
                    }
                }
                else
                {
                    // 4-component CMYK / YCCK.
                    double c0 = Sample(planes[0], planeW[0], x, y, comps[0], hMax, vMax);
                    double c1 = Sample(planes[1], planeW[1], x, y, comps[1], hMax, vMax);
                    double c2 = Sample(planes[2], planeW[2], x, y, comps[2], hMax, vMax);
                    double k = Sample(planes[3], planeW[3], x, y, comps[3], hMax, vMax);

                    // Adobe (APP14 present) stores CMYK/YCCK INVERTED; un-invert to true samples.
                    if (meta.AdobeTransform >= 0)
                    {
                        c0 = 255.0 - c0;
                        c1 = 255.0 - c1;
                        c2 = 255.0 - c2;
                        k = 255.0 - k;
                    }

                    double cc, cm, cyl;
                    if (meta.AdobeTransform == 2)
                    {
                        // YCCK: YCbCr → RGB, then CMY = 255 − RGB.
                        YccToRgbD(c0, c1, c2, out double rr, out double gg, out double bb);
                        cc = 255.0 - rr;
                        cm = 255.0 - gg;
                        cyl = 255.0 - bb;
                    }
                    else
                    {
                        cc = c0;
                        cm = c1;
                        cyl = c2;
                    }

                    rgb[o] = Clamp((int)Math.Round((255.0 - cc) * (255.0 - k) / 255.0));
                    rgb[o + 1] = Clamp((int)Math.Round((255.0 - cm) * (255.0 - k) / 255.0));
                    rgb[o + 2] = Clamp((int)Math.Round((255.0 - cyl) * (255.0 - k) / 255.0));
                }
            }
        }

        return new RasterImage { Width = w, Height = h, Components = 3, Samples = rgb };
    }

    private static void YccToRgb(double y, double cb, double cr, out int r, out int g, out int b)
    {
        YccToRgbD(y, cb, cr, out double rd, out double gd, out double bd);
        r = (int)Math.Round(rd);
        g = (int)Math.Round(gd);
        b = (int)Math.Round(bd);
    }

    private static void YccToRgbD(double y, double cb, double cr, out double r, out double g, out double b)
    {
        double cbv = cb - 128.0;
        double crv = cr - 128.0;
        r = y + (1.402 * crv);
        g = y - (0.344136 * cbv) - (0.714136 * crv);
        b = y + (1.772 * cbv);
    }

    private static double Sample(byte[] plane, int planeWidth, int x, int y, JpegComponent comp, int hMax, int vMax)
    {
        int sx = (x * comp.HorizontalSampling) / hMax;
        int sy = (y * comp.VerticalSampling) / vMax;
        return plane[(sy * planeWidth) + sx];
    }

    private static byte Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

    private static int Extend(int value, int size)
    {
        int threshold = 1 << (size - 1);
        return value < threshold ? value - (1 << size) + 1 : value;
    }

    private static int[] OnesQuant()
    {
        var q = new int[64];
        for (int i = 0; i < 64; i++)
        {
            q[i] = 1;
        }

        return q;
    }

    private static double[,] BuildCosLut()
    {
        var lut = new double[8, 8];
        for (int u = 0; u < 8; u++)
        {
            for (int x = 0; x < 8; x++)
            {
                lut[u, x] = Math.Cos(((2 * x) + 1) * u * Math.PI / 16.0);
            }
        }

        return lut;
    }

    private static double[] BuildCuLut()
    {
        var cu = new double[8];
        cu[0] = 1.0 / Math.Sqrt(2.0);
        for (int u = 1; u < 8; u++)
        {
            cu[u] = 1.0;
        }

        return cu;
    }

    /// <summary>A canonical Huffman decode table (ITU-T T.81 Annex F).</summary>
    private sealed class HuffTable
    {
        private readonly int[] _minCode = new int[17];
        private readonly int[] _maxCode = new int[17];   // -1 when no codes of that length
        private readonly int[] _valPtr = new int[17];
        private byte[] _symbols = Array.Empty<byte>();

        public static HuffTable Build(byte[] counts, byte[] symbols)
        {
            var table = new HuffTable { _symbols = symbols };
            int code = 0;
            int k = 0;
            for (int len = 1; len <= 16; len++)
            {
                int count = counts[len - 1];
                if (count > 0)
                {
                    table._valPtr[len] = k;
                    table._minCode[len] = code;
                    code += count;
                    table._maxCode[len] = code - 1;
                    k += count;
                }
                else
                {
                    table._maxCode[len] = -1;
                }

                code <<= 1;
            }

            return table;
        }

        public int Decode(BitReader r)
        {
            int code = 0;
            for (int len = 1; len <= 16; len++)
            {
                code = (code << 1) | r.ReadBit();
                if (_maxCode[len] >= 0 && code <= _maxCode[len])
                {
                    int index = _valPtr[len] + (code - _minCode[len]);
                    return index >= 0 && index < _symbols.Length ? _symbols[index] : 0;
                }
            }

            return 0;   // invalid code — degrade to 0 rather than throw mid-scan
        }
    }

    /// <summary>An MSB-first bit reader over entropy-coded JPEG data, handling 0xFF00 stuffing and restart markers.</summary>
    private sealed class BitReader
    {
        private readonly byte[] _data;
        private int _pos;
        private int _bitBuffer;
        private int _bitCount;
        private bool _atMarker;

        public BitReader(byte[] data, int start)
        {
            _data = data;
            _pos = start;
        }

        public int ReadBit()
        {
            if (_bitCount == 0 && !FillByte())
            {
                return 0;   // exhausted / at a marker — supply 0 bits
            }

            _bitCount--;
            return (_bitBuffer >> _bitCount) & 1;
        }

        public int Receive(int count)
        {
            int value = 0;
            for (int i = 0; i < count; i++)
            {
                value = (value << 1) | ReadBit();
            }

            return value;
        }

        public void Restart()
        {
            _bitCount = 0;
            _atMarker = false;
            while (_pos + 1 < _data.Length && !(_data[_pos] == 0xFF && _data[_pos + 1] >= 0xD0 && _data[_pos + 1] <= 0xD7))
            {
                _pos++;
            }

            if (_pos + 1 < _data.Length)
            {
                _pos += 2;   // skip the RSTn marker
            }
        }

        private bool FillByte()
        {
            if (_atMarker || _pos >= _data.Length)
            {
                return false;
            }

            int b = _data[_pos++];
            if (b == 0xFF)
            {
                int next = _pos < _data.Length ? _data[_pos] : 0xD9;
                if (next == 0x00)
                {
                    _pos++;             // stuffed byte → literal 0xFF
                }
                else
                {
                    _atMarker = true;   // a real marker (RSTn / EOI) follows
                    _pos--;             // leave position on the 0xFF for Restart()
                    return false;
                }
            }

            _bitBuffer = b;
            _bitCount = 8;
            return true;
        }
    }
}
