using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZeroDep.Fonts;

/// <summary>
/// Pure-BCL parser for a CFF font program (PDF <c>/FontFile3</c> Type1C, or the <c>CFF </c> table of an
/// OpenType 'OTTO' font; Adobe Technical Note #5176/#5177). Parses the INDEX/DICT structures and runs the
/// Type 2 charstring interpreter to produce a cubic <see cref="GlyphOutline"/> per glyph. Handles both plain
/// and CID-keyed fonts: a CID-keyed CFF (ROS / FDArray / FDSelect) resolves each glyph's private dict and
/// local subrs from its selected font DICT (ADR-0005 §F5).
/// </summary>
public sealed class CffFont
{
    private const int MaxSubrDepth = 10;

    private readonly byte[] _data;
    private readonly CffIndex _charStrings;
    private readonly CffIndex _globalSubrs;
    private readonly int _globalBias;
    private readonly FontDict[] _fds;
    private readonly int[]? _fdSelect;
    private readonly int[]? _charset; // GID -> CID (CID-keyed only)

    private CffFont(byte[] data, CffIndex charStrings, CffIndex globalSubrs, FontDict[] fds, int[]? fdSelect, int[]? charset, int unitsPerEm, bool isCid)
    {
        _data = data;
        _charStrings = charStrings;
        _globalSubrs = globalSubrs;
        _globalBias = Bias(globalSubrs.Count);
        _fds = fds;
        _fdSelect = fdSelect;
        _charset = charset;
        UnitsPerEm = unitsPerEm;
        IsCidKeyed = isCid;
    }

    /// <summary>The em square size in font units (1000 for standard CFF).</summary>
    public int UnitsPerEm { get; }

    /// <summary>True if this is a CID-keyed CFF (has ROS / FDArray / FDSelect).</summary>
    public bool IsCidKeyed { get; }

    /// <summary>The number of glyphs (CharStrings).</summary>
    public int GlyphCount => _charStrings.Count;

    /// <summary>For a CID-keyed font, maps a CID to its glyph id; returns 0 if not found or not CID-keyed.</summary>
    /// <param name="cid">The character identifier.</param>
    public int GlyphIdForCid(int cid)
    {
        if (_charset is null)
        {
            return cid >= 0 && cid < _charStrings.Count ? cid : 0;
        }

        for (int gid = 0; gid < _charset.Length; gid++)
        {
            if (_charset[gid] == cid)
            {
                return gid;
            }
        }

        return 0;
    }

    private sealed class FontDict
    {
        public CffIndex LocalSubrs { get; set; } = CffIndex.Empty;

        public int LocalBias { get; set; }

        public double NominalWidthX { get; set; }

        public double DefaultWidthX { get; set; }
    }

    /// <summary>Parses a CFF program (bare CFF, or the <c>CFF </c> table of an 'OTTO' OpenType font).</summary>
    /// <param name="input">The CFF/OpenType bytes.</param>
    public static CffFont Load(byte[] input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        byte[] data = UnwrapOpenType(input);
        int hdrSize = data.Length > 2 ? data[2] : 4;

        int pos = hdrSize;
        pos = CffIndex.Read(data, pos, out _);                 // Name INDEX
        pos = CffIndex.Read(data, pos, out CffIndex topDicts); // Top DICT INDEX
        pos = CffIndex.Read(data, pos, out _);                 // String INDEX
        CffIndex.Read(data, pos, out CffIndex globalSubrs);    // Global Subr INDEX

        (int ts, int tl) = topDicts.Item(0);
        Dictionary<int, double[]> top = ParseDict(data, ts, ts + tl);

        int charStringsOff = top.TryGetValue(17, out double[]? cs) && cs.Length > 0 ? (int)cs[0] : 0;
        CffIndex.Read(data, charStringsOff, out CffIndex charStrings);

        int unitsPerEm = 1000;
        if (top.TryGetValue(1207, out double[]? matrix) && matrix.Length > 0 && matrix[0] != 0)
        {
            unitsPerEm = (int)Math.Round(1.0 / matrix[0]);
        }

        bool isCid = top.ContainsKey(1230); // ROS
        if (isCid)
        {
            FontDict[] fds = ReadFdArray(data, top);
            int[] fdSelect = ReadFdSelect(data, top, charStrings.Count);
            int[] charset = ReadCharset(data, top, charStrings.Count);
            return new CffFont(data, charStrings, globalSubrs, fds, fdSelect, charset, unitsPerEm, true);
        }

        var fd = new FontDict();
        if (top.TryGetValue(18, out double[]? priv) && priv.Length >= 2)
        {
            FillPrivate(data, (int)priv[1], (int)priv[0], fd);
        }

        return new CffFont(data, charStrings, globalSubrs, new[] { fd }, null, null, unitsPerEm, false);
    }

    private static void FillPrivate(byte[] data, int privOff, int privSize, FontDict fd)
    {
        Dictionary<int, double[]> pd = ParseDict(data, privOff, privOff + privSize);
        fd.DefaultWidthX = pd.TryGetValue(20, out double[]? dw) && dw.Length > 0 ? dw[0] : 0;
        fd.NominalWidthX = pd.TryGetValue(21, out double[]? nw) && nw.Length > 0 ? nw[0] : 0;
        if (pd.TryGetValue(19, out double[]? subrs) && subrs.Length > 0)
        {
            CffIndex.Read(data, privOff + (int)subrs[0], out CffIndex local);
            fd.LocalSubrs = local;
            fd.LocalBias = Bias(local.Count);
        }
    }

    private static FontDict[] ReadFdArray(byte[] data, Dictionary<int, double[]> top)
    {
        if (!top.TryGetValue(1236, out double[]? fda) || fda.Length == 0)
        {
            return new[] { new FontDict() };
        }

        CffIndex.Read(data, (int)fda[0], out CffIndex fdIndex);
        int count = Math.Max(1, fdIndex.Count);
        var fds = new FontDict[count];
        for (int i = 0; i < fdIndex.Count; i++)
        {
            var fd = new FontDict();
            (int s, int l) = fdIndex.Item(i);
            Dictionary<int, double[]> d = ParseDict(data, s, s + l);
            if (d.TryGetValue(18, out double[]? priv) && priv.Length >= 2)
            {
                FillPrivate(data, (int)priv[1], (int)priv[0], fd);
            }

            fds[i] = fd;
        }

        for (int i = 0; i < count; i++)
        {
            fds[i] ??= new FontDict();
        }

        return fds;
    }

    private static int[] ReadFdSelect(byte[] data, Dictionary<int, double[]> top, int nGlyphs)
    {
        var sel = new int[nGlyphs];
        if (!top.TryGetValue(1237, out double[]? fs) || fs.Length == 0)
        {
            return sel;
        }

        var r = new BigEndianReader(data) { Position = (int)fs[0] };
        int format = r.ReadU8();
        if (format == 0)
        {
            for (int i = 0; i < nGlyphs && r.Position < data.Length; i++)
            {
                sel[i] = r.ReadU8();
            }
        }
        else if (format == 3)
        {
            int nRanges = r.ReadU16();
            int first = r.ReadU16();
            for (int i = 0; i < nRanges; i++)
            {
                int fd = r.ReadU8();
                int next = r.ReadU16();
                for (int g = first; g < next && g < nGlyphs; g++)
                {
                    if (g >= 0)
                    {
                        sel[g] = fd;
                    }
                }

                first = next;
            }
        }

        return sel;
    }

    private static int[] ReadCharset(byte[] data, Dictionary<int, double[]> top, int nGlyphs)
    {
        var charset = new int[nGlyphs];
        int off = top.TryGetValue(15, out double[]? c) && c.Length > 0 ? (int)c[0] : 0;
        if (off == 0)
        {
            // ISOAdobe / predefined: identity is a reasonable fallback for CID geometry-by-GID.
            for (int i = 0; i < nGlyphs; i++)
            {
                charset[i] = i;
            }

            return charset;
        }

        var r = new BigEndianReader(data) { Position = off };
        int format = r.ReadU8();
        // gid 0 is always .notdef -> CID 0
        int gid = 1;
        if (format == 0)
        {
            while (gid < nGlyphs && r.Position + 1 < data.Length)
            {
                charset[gid++] = r.ReadU16();
            }
        }
        else if (format == 1 || format == 2)
        {
            while (gid < nGlyphs && r.Position < data.Length)
            {
                int firstCid = r.ReadU16();
                int left = format == 1 ? r.ReadU8() : r.ReadU16();
                for (int i = 0; i <= left && gid < nGlyphs; i++)
                {
                    charset[gid++] = firstCid + i;
                }
            }
        }

        return charset;
    }

    /// <summary>Returns the outline of a glyph (font units), or <see cref="GlyphOutline.Empty"/> on fault.</summary>
    /// <param name="glyphId">The glyph id.</param>
    public GlyphOutline GetGlyph(int glyphId)
    {
        if (glyphId < 0 || glyphId >= _charStrings.Count)
        {
            return GlyphOutline.Empty;
        }

        var interp = new Type2Interpreter(this);
        return interp.Run(glyphId);
    }

    private static int Bias(int count) => count < 1240 ? 107 : (count < 33900 ? 1131 : 32768);

    private static byte[] UnwrapOpenType(byte[] data)
    {
        if (data.Length < 4 || !(data[0] == 0x4F && data[1] == 0x54 && data[2] == 0x54 && data[3] == 0x4F))
        {
            return data; // not 'OTTO'
        }

        var r = new BigEndianReader(data) { Position = 4 };
        int numTables = r.ReadU16();
        r.Position = 12;
        for (int i = 0; i < numTables; i++)
        {
            char a = (char)r.ReadU8();
            char b = (char)r.ReadU8();
            char c = (char)r.ReadU8();
            char d = (char)r.ReadU8();
            r.ReadU32();
            int offset = (int)r.ReadU32();
            int length = (int)r.ReadU32();
            if (a == 'C' && b == 'F' && c == 'F' && d == ' ')
            {
                var cff = new byte[length];
                Array.Copy(data, offset, cff, 0, Math.Min(length, data.Length - offset));
                return cff;
            }
        }

        return data;
    }

    // ---- DICT parsing ----

    private static Dictionary<int, double[]> ParseDict(byte[] data, int start, int end)
    {
        var dict = new Dictionary<int, double[]>();
        var operands = new List<double>();
        int p = start;
        while (p < end && p < data.Length)
        {
            int b0 = data[p];
            if (b0 <= 21)
            {
                int op = b0;
                p++;
                if (b0 == 12)
                {
                    op = 1200 + data[p];
                    p++;
                }

                dict[op] = operands.ToArray();
                operands.Clear();
            }
            else
            {
                p = ReadDictNumber(data, p, out double value);
                operands.Add(value);
            }
        }

        return dict;
    }

    private static int ReadDictNumber(byte[] d, int p, out double value)
    {
        int b0 = d[p];
        if (b0 == 28)
        {
            value = (short)((d[p + 1] << 8) | d[p + 2]);
            return p + 3;
        }

        if (b0 == 29)
        {
            value = (d[p + 1] << 24) | (d[p + 2] << 16) | (d[p + 3] << 8) | d[p + 4];
            return p + 5;
        }

        if (b0 == 30)
        {
            return ReadRealNumber(d, p + 1, out value);
        }

        if (b0 >= 32 && b0 <= 246)
        {
            value = b0 - 139;
            return p + 1;
        }

        if (b0 >= 247 && b0 <= 250)
        {
            value = ((b0 - 247) * 256) + d[p + 1] + 108;
            return p + 2;
        }

        if (b0 >= 251 && b0 <= 254)
        {
            value = (-(b0 - 251) * 256) - d[p + 1] - 108;
            return p + 2;
        }

        value = 0;
        return p + 1;
    }

    private static int ReadRealNumber(byte[] d, int p, out double value)
    {
        var sb = new StringBuilder();
        bool done = false;
        while (!done && p < d.Length)
        {
            int b = d[p++];
            for (int half = 0; half < 2; half++)
            {
                int nib = half == 0 ? (b >> 4) & 0xF : b & 0xF;
                switch (nib)
                {
                    case <= 9: sb.Append((char)('0' + nib)); break;
                    case 0xa: sb.Append('.'); break;
                    case 0xb: sb.Append('E'); break;
                    case 0xc: sb.Append("E-"); break;
                    case 0xe: sb.Append('-'); break;
                    case 0xf: done = true; break;
                    default: break;
                }

                if (done)
                {
                    break;
                }
            }
        }

        value = double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
        return p;
    }

    // ---- Type 2 charstring interpreter ----

    private sealed class Type2Interpreter
    {
        private readonly CffFont _font;
        private readonly List<double> _stack = new List<double>();
        private readonly ContourBuilder _builder = new ContourBuilder();
        private CffIndex _localSubrs;
        private int _localBias;
        private double _nominalWidthX;
        private double _x;
        private double _y;
        private int _stems;
        private bool _haveWidth;
        private double _width;

        public Type2Interpreter(CffFont font)
        {
            _font = font;
            _localSubrs = CffIndex.Empty;
        }

        public GlyphOutline Run(int glyphId)
        {
            int fdIndex = _font._fdSelect is { } sel && glyphId < sel.Length ? sel[glyphId] : 0;
            FontDict fd = fdIndex >= 0 && fdIndex < _font._fds.Length ? _font._fds[fdIndex] : _font._fds[0];
            _localSubrs = fd.LocalSubrs;
            _localBias = fd.LocalBias;
            _nominalWidthX = fd.NominalWidthX;
            _width = fd.DefaultWidthX;

            (int start, int len) = _font._charStrings.Item(glyphId);
            try
            {
                Execute(start, start + len, 0);
            }
            catch (Exception)
            {
                // a malformed charstring degrades to whatever was built so far
            }

            return new GlyphOutline(_builder.Build(), (int)Math.Round(_width));
        }

        private bool Execute(int start, int end, int depth)
        {
            if (depth > MaxSubrDepth)
            {
                return true;
            }

            byte[] d = _font._data;
            int p = start;
            while (p < end && p < d.Length)
            {
                int b0 = d[p++];
                if (b0 >= 32 || b0 == 28)
                {
                    p = ReadOperand(d, b0, p);
                    continue;
                }

                switch (b0)
                {
                    case 1:
                    case 3:
                    case 18:
                    case 23:
                        CountStems();
                        break;
                    case 19:
                    case 20:
                        CountStems();
                        p += (_stems + 7) / 8;
                        break;
                    case 21: RMoveTo(); break;
                    case 22: HVMoveTo(true); break;
                    case 4: HVMoveTo(false); break;
                    case 5: RLineTo(); break;
                    case 6: AlternatingLineTo(true); break;
                    case 7: AlternatingLineTo(false); break;
                    case 8: RrCurveTo(); break;
                    case 24: RCurveLine(); break;
                    case 25: RLineCurve(); break;
                    case 26: VvCurveTo(); break;
                    case 27: HhCurveTo(); break;
                    case 30: AlternatingCurveTo(false); break;
                    case 31: AlternatingCurveTo(true); break;
                    case 10:
                    {
                        int idx = (int)Pop() + _localBias;
                        if (idx >= 0 && idx < _localSubrs.Count)
                        {
                            (int s, int l) = _localSubrs.Item(idx);
                            if (Execute(s, s + l, depth + 1))
                            {
                                return true;
                            }
                        }

                        break;
                    }

                    case 29:
                    {
                        int idx = (int)Pop() + _font._globalBias;
                        if (idx >= 0 && idx < _font._globalSubrs.Count)
                        {
                            (int s, int l) = _font._globalSubrs.Item(idx);
                            if (Execute(s, s + l, depth + 1))
                            {
                                return true;
                            }
                        }

                        break;
                    }

                    case 11:
                        return false; // return
                    case 14:
                        EndChar();
                        return true;
                    case 12:
                        Escape(d[p++]);
                        break;
                    default:
                        _stack.Clear();
                        break;
                }
            }

            return false;
        }

        private int ReadOperand(byte[] d, int b0, int p)
        {
            if (b0 == 28)
            {
                _stack.Add((short)((d[p] << 8) | d[p + 1]));
                return p + 2;
            }

            if (b0 < 247)
            {
                _stack.Add(b0 - 139);
                return p;
            }

            if (b0 < 251)
            {
                _stack.Add(((b0 - 247) * 256) + d[p] + 108);
                return p + 1;
            }

            if (b0 < 255)
            {
                _stack.Add((-(b0 - 251) * 256) - d[p] - 108);
                return p + 1;
            }

            int fixed32 = (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];
            _stack.Add(fixed32 / 65536.0);
            return p + 4;
        }

        private double Pop()
        {
            if (_stack.Count == 0)
            {
                return 0;
            }

            double v = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);
            return v;
        }

        private void CountStems()
        {
            if (!_haveWidth && (_stack.Count & 1) == 1)
            {
                TakeWidth(0);
            }

            _haveWidth = true;
            _stems += _stack.Count / 2;
            _stack.Clear();
        }

        private void TakeWidth(int index)
        {
            _width = _nominalWidthX + _stack[index];
            _stack.RemoveAt(index);
        }

        private void MoveWidth(int expected)
        {
            if (!_haveWidth && _stack.Count > expected)
            {
                TakeWidth(0);
            }

            _haveWidth = true;
        }

        private void RMoveTo()
        {
            MoveWidth(2);
            if (_stack.Count >= 2)
            {
                _x += _stack[0];
                _y += _stack[1];
                _builder.MoveTo(_x, _y);
            }

            _stack.Clear();
        }

        private void HVMoveTo(bool horizontal)
        {
            MoveWidth(1);
            if (_stack.Count >= 1)
            {
                if (horizontal)
                {
                    _x += _stack[0];
                }
                else
                {
                    _y += _stack[0];
                }

                _builder.MoveTo(_x, _y);
            }

            _stack.Clear();
        }

        private void RLineTo()
        {
            for (int i = 0; i + 1 < _stack.Count; i += 2)
            {
                _x += _stack[i];
                _y += _stack[i + 1];
                _builder.LineTo(_x, _y);
            }

            _stack.Clear();
        }

        private void AlternatingLineTo(bool horizontal)
        {
            for (int i = 0; i < _stack.Count; i++)
            {
                if (horizontal)
                {
                    _x += _stack[i];
                }
                else
                {
                    _y += _stack[i];
                }

                _builder.LineTo(_x, _y);
                horizontal = !horizontal;
            }

            _stack.Clear();
        }

        private void RrCurveTo()
        {
            int i = 0;
            while (i + 6 <= _stack.Count)
            {
                Curve(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                i += 6;
            }

            _stack.Clear();
        }

        private void RCurveLine()
        {
            int i = 0;
            while (i + 6 <= _stack.Count - 2)
            {
                Curve(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);
                i += 6;
            }

            if (i + 1 < _stack.Count)
            {
                _x += _stack[i];
                _y += _stack[i + 1];
                _builder.LineTo(_x, _y);
            }

            _stack.Clear();
        }

        private void RLineCurve()
        {
            int i = 0;
            while (i + 2 <= _stack.Count - 6)
            {
                _x += _stack[i];
                _y += _stack[i + 1];
                _builder.LineTo(_x, _y);
                i += 2;
            }

            if (i + 6 <= _stack.Count)
            {
                Curve(_stack[i], _stack[i + 1], _stack[i + 2], _stack[i + 3], _stack[i + 4], _stack[i + 5]);
            }

            _stack.Clear();
        }

        private void HhCurveTo()
        {
            int i = 0;
            double dy1 = 0;
            if ((_stack.Count & 1) == 1)
            {
                dy1 = _stack[0];
                i = 1;
            }

            bool first = true;
            while (i + 4 <= _stack.Count)
            {
                double c1x = _x + _stack[i];
                double c1y = _y + (first ? dy1 : 0);
                double c2x = c1x + _stack[i + 1];
                double c2y = c1y + _stack[i + 2];
                double ex = c2x + _stack[i + 3];
                double ey = c2y;
                AppendCubic(c1x, c1y, c2x, c2y, ex, ey);
                i += 4;
                first = false;
            }

            _stack.Clear();
        }

        private void VvCurveTo()
        {
            int i = 0;
            double dx1 = 0;
            if ((_stack.Count & 1) == 1)
            {
                dx1 = _stack[0];
                i = 1;
            }

            bool first = true;
            while (i + 4 <= _stack.Count)
            {
                double c1x = _x + (first ? dx1 : 0);
                double c1y = _y + _stack[i];
                double c2x = c1x + _stack[i + 1];
                double c2y = c1y + _stack[i + 2];
                double ex = c2x;
                double ey = c2y + _stack[i + 3];
                AppendCubic(c1x, c1y, c2x, c2y, ex, ey);
                i += 4;
                first = false;
            }

            _stack.Clear();
        }

        private void AlternatingCurveTo(bool horizontal)
        {
            int i = 0;
            int n = _stack.Count;
            while (i + 4 <= n)
            {
                int remaining = n - i;
                double c1x;
                double c1y;
                double c2x;
                double c2y;
                double ex;
                double ey;
                if (horizontal)
                {
                    c1x = _x + _stack[i];
                    c1y = _y;
                    c2x = c1x + _stack[i + 1];
                    c2y = c1y + _stack[i + 2];
                    ey = c2y + _stack[i + 3];
                    ex = remaining == 5 ? c2x + _stack[i + 4] : c2x;
                }
                else
                {
                    c1x = _x;
                    c1y = _y + _stack[i];
                    c2x = c1x + _stack[i + 1];
                    c2y = c1y + _stack[i + 2];
                    ex = c2x + _stack[i + 3];
                    ey = remaining == 5 ? c2y + _stack[i + 4] : c2y;
                }

                AppendCubic(c1x, c1y, c2x, c2y, ex, ey);
                _x = ex;
                _y = ey;
                horizontal = !horizontal;
                i += 4;
            }

            _stack.Clear();
        }

        private void Curve(double dxa, double dya, double dxb, double dyb, double dxc, double dyc)
        {
            double c1x = _x + dxa;
            double c1y = _y + dya;
            double c2x = c1x + dxb;
            double c2y = c1y + dyb;
            _x = c2x + dxc;
            _y = c2y + dyc;
            _builder.CubicTo(c1x, c1y, c2x, c2y, _x, _y);
        }

        private void AppendCubic(double c1x, double c1y, double c2x, double c2y, double ex, double ey)
        {
            _x = ex;
            _y = ey;
            _builder.CubicTo(c1x, c1y, c2x, c2y, ex, ey);
        }

        private void EndChar()
        {
            if (!_haveWidth && (_stack.Count == 1 || _stack.Count == 5))
            {
                TakeWidth(0);
            }

            _haveWidth = true;
            _stack.Clear();
        }

        private void Escape(int op)
        {
            switch (op)
            {
                case 35: Flex(); break;
                case 34: HFlex(); break;
                case 36: HFlex1(); break;
                case 37: Flex1(); break;
                default: _stack.Clear(); break;
            }
        }

        private void Flex()
        {
            if (_stack.Count >= 12)
            {
                Curve(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5]);
                Curve(_stack[6], _stack[7], _stack[8], _stack[9], _stack[10], _stack[11]);
            }

            _stack.Clear();
        }

        private void HFlex()
        {
            if (_stack.Count >= 7)
            {
                double startY = _y;
                Curve(_stack[0], 0, _stack[1], _stack[2], _stack[3], 0);
                Curve(_stack[4], 0, _stack[5], startY - _y, _stack[6], 0);
            }

            _stack.Clear();
        }

        private void HFlex1()
        {
            if (_stack.Count >= 9)
            {
                double startY = _y;
                Curve(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], 0);
                Curve(_stack[5], 0, _stack[6], _stack[7], _stack[8], startY - (_y + _stack[7]));
            }

            _stack.Clear();
        }

        private void Flex1()
        {
            if (_stack.Count >= 11)
            {
                double startX = _x;
                double startY = _y;
                double dx = 0;
                double dy = 0;
                for (int i = 0; i < 10; i += 2)
                {
                    dx += _stack[i];
                    dy += _stack[i + 1];
                }

                Curve(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5]);
                if (Math.Abs(dx) > Math.Abs(dy))
                {
                    Curve(_stack[6], _stack[7], _stack[8], _stack[9], _stack[10], startY - _y - _stack[7] - _stack[9]);
                }
                else
                {
                    Curve(_stack[6], _stack[7], _stack[8], _stack[9], startX - _x - _stack[6] - _stack[8], _stack[10]);
                }
            }

            _stack.Clear();
        }
    }
}
