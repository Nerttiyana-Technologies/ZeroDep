using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZeroDep.Fonts;

/// <summary>
/// Parses an embedded PDF Type 1 font program (the <c>FontFile</c> stream): clear-text header, eexec-encrypted
/// private section, and charstring-encrypted glyph programs. Glyphs are name-keyed (Type 1 has no GIDs).
/// Charstrings are interpreted to cubic <see cref="GlyphOutline"/>s, including flex, hint replacement, and the
/// <c>seac</c> accented-composite operator (ADR-0005 §F4).
/// </summary>
public sealed class Type1Font
{
    private readonly Dictionary<string, byte[]> _charstrings;
    private readonly byte[][] _subrs;
    private readonly List<string> _glyphNames;

    private Type1Font(int unitsPerEm, Dictionary<string, byte[]> charstrings, byte[][] subrs)
    {
        UnitsPerEm = unitsPerEm;
        _charstrings = charstrings;
        _subrs = subrs;
        _glyphNames = new List<string>(charstrings.Keys);
        _glyphNames.Sort(StringComparer.Ordinal);
    }

    /// <summary>The em-square size derived from the FontMatrix (typically 1000).</summary>
    public int UnitsPerEm { get; }

    /// <summary>The glyph names present in the font, sorted ordinally.</summary>
    public IReadOnlyList<string> GlyphNames => _glyphNames;

    /// <summary>The number of named glyphs.</summary>
    public int GlyphCount => _glyphNames.Count;

    /// <summary>True if the font defines a glyph with the given name.</summary>
    public bool HasGlyph(string name) => _charstrings.ContainsKey(name);

    /// <summary>Parses a Type 1 font program from raw <c>FontFile</c> bytes.</summary>
    public static Type1Font Load(byte[] data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        int eexec = IndexOf(data, "eexec", 0);
        if (eexec < 0)
        {
            throw new NotSupportedException("Not a Type 1 font program (no eexec).");
        }

        int unitsPerEm = ParseUnitsPerEm(data, eexec);

        // Locate the start of the eexec binary (skip whitespace after "eexec").
        int p = eexec + 5;
        while (p < data.Length && IsWhite(data[p]))
        {
            p++;
        }

        byte[] enc = DetectAndCollect(data, p);
        byte[] priv = DecryptEexec(enc);

        int lenIV = ParseLenIV(priv);
        byte[][] subrs = ParseSubrs(priv, lenIV);
        Dictionary<string, byte[]> cs = ParseCharStrings(priv, lenIV);
        if (cs.Count == 0)
        {
            throw new NotSupportedException("Type 1 font has no CharStrings.");
        }

        return new Type1Font(unitsPerEm, cs, subrs);
    }

    /// <summary>Returns the outline for the named glyph, or <see cref="GlyphOutline.Empty"/> if absent/empty.</summary>
    public GlyphOutline GetGlyph(string name)
    {
        if (name is null || !_charstrings.ContainsKey(name))
        {
            return GlyphOutline.Empty;
        }

        var interp = new Type1Interpreter(this);
        interp.Run(_charstrings[name]);
        IReadOnlyList<GlyphContour> contours = interp.Builder.Build();
        return contours.Count == 0 ? GlyphOutline.Empty : new GlyphOutline(contours, (int)Math.Round(interp.Width));
    }

    private byte[]? Subr(int i) => i >= 0 && i < _subrs.Length ? _subrs[i] : null;

    private bool TryCharString(string name, out byte[] cs) => _charstrings.TryGetValue(name, out cs!);

    // ---- eexec / charstring decryption ----------------------------------

    private static byte[] DecryptEexec(byte[] cipher)
    {
        ushort r = 55665;
        var outb = new byte[cipher.Length];
        for (int i = 0; i < cipher.Length; i++)
        {
            byte c = cipher[i];
            outb[i] = (byte)(c ^ (r >> 8));
            r = (ushort)(((c + r) * 52845) + 22719);
        }

        // Skip the 4 random lead bytes.
        int skip = Math.Min(4, outb.Length);
        var res = new byte[outb.Length - skip];
        Array.Copy(outb, skip, res, 0, res.Length);
        return res;
    }

    private static byte[] DecryptCharString(byte[] cipher, int offset, int length, int lenIV)
    {
        ushort r = 4330;
        var outb = new byte[length];
        for (int i = 0; i < length; i++)
        {
            byte c = cipher[offset + i];
            outb[i] = (byte)(c ^ (r >> 8));
            r = (ushort)(((c + r) * 52845) + 22719);
        }

        int skip = Math.Min(lenIV < 0 ? 0 : lenIV, outb.Length);
        var res = new byte[outb.Length - skip];
        Array.Copy(outb, skip, res, 0, res.Length);
        return res;
    }

    private static byte[] DetectAndCollect(byte[] data, int start)
    {
        // Hex eexec if the first 4 significant bytes are all hex digits.
        int probe = start, hex = 0, sig = 0;
        while (probe < data.Length && sig < 4)
        {
            byte b = data[probe++];
            if (IsWhite(b))
            {
                continue;
            }

            sig++;
            if (IsHex(b))
            {
                hex++;
            }
        }

        if (hex == 4)
        {
            var bytes = new List<byte>(data.Length - start);
            int hi = -1;
            for (int i = start; i < data.Length; i++)
            {
                byte b = data[i];
                if (!IsHex(b))
                {
                    if (IsWhite(b))
                    {
                        continue;
                    }

                    break;
                }

                int v = HexVal(b);
                if (hi < 0)
                {
                    hi = v;
                }
                else
                {
                    bytes.Add((byte)((hi << 4) | v));
                    hi = -1;
                }
            }

            return bytes.ToArray();
        }

        var res = new byte[data.Length - start];
        Array.Copy(data, start, res, 0, res.Length);
        return res;
    }

    // ---- clear-text parsing --------------------------------------------

    private static int ParseUnitsPerEm(byte[] data, int limit)
    {
        int m = IndexOf(data, "/FontMatrix", 0);
        if (m < 0 || m > limit)
        {
            return 1000;
        }

        int lb = Array.IndexOf(data, (byte)'[', m);
        if (lb < 0)
        {
            return 1000;
        }

        int rb = Array.IndexOf(data, (byte)']', lb);
        if (rb < 0)
        {
            return 1000;
        }

        string body = Ascii(data, lb + 1, rb - lb - 1);
        string[] parts = body.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double a) && a > 0)
        {
            return (int)Math.Round(1.0 / a);
        }

        return 1000;
    }

    private static int ParseLenIV(byte[] priv)
    {
        int i = IndexOf(priv, "/lenIV", 0);
        if (i < 0)
        {
            return 4;
        }

        i += 6;
        return ReadIntAt(priv, ref i, out int v) ? v : 4;
    }

    private static byte[][] ParseSubrs(byte[] priv, int lenIV)
    {
        int s = IndexOf(priv, "/Subrs", 0);
        if (s < 0)
        {
            return Array.Empty<byte[]>();
        }

        int i = s + 6;
        if (!ReadIntAt(priv, ref i, out int count) || count <= 0 || count > 65536)
        {
            return Array.Empty<byte[]>();
        }

        var subrs = new byte[count][];
        int found = 0;
        while (found < count)
        {
            int dup = IndexOf(priv, "dup ", i);
            if (dup < 0)
            {
                break;
            }

            i = dup + 4;
            if (!ReadIntAt(priv, ref i, out int idx) || !ReadIntAt(priv, ref i, out int len))
            {
                break;
            }

            if (!SkipBinaryIntro(priv, ref i))
            {
                break;
            }

            if (idx >= 0 && idx < count && i + len <= priv.Length)
            {
                subrs[idx] = DecryptCharString(priv, i, len, lenIV);
            }

            i += len;
            found++;
        }

        for (int k = 0; k < count; k++)
        {
            subrs[k] ??= Array.Empty<byte>();
        }

        return subrs;
    }

    private static Dictionary<string, byte[]> ParseCharStrings(byte[] priv, int lenIV)
    {
        var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        int c = IndexOf(priv, "/CharStrings", 0);
        if (c < 0)
        {
            return map;
        }

        int i = c + 12;
        ReadIntAt(priv, ref i, out _); // count (advisory)

        // Move past "begin".
        int begin = IndexOf(priv, "begin", i);
        if (begin >= 0)
        {
            i = begin + 5;
        }

        while (i < priv.Length)
        {
            // Find the next name token starting with '/'.
            int slash = Array.IndexOf(priv, (byte)'/', i);
            if (slash < 0)
            {
                break;
            }

            int end = IndexOf(priv, "end", i);
            if (end >= 0 && end < slash)
            {
                break;
            }

            int j = slash + 1;
            int nameStart = j;
            while (j < priv.Length && !IsWhite(priv[j]) && priv[j] != '{' && priv[j] != '(')
            {
                j++;
            }

            string name = Ascii(priv, nameStart, j - nameStart);
            if (!ReadIntAt(priv, ref j, out int len))
            {
                i = j;
                continue;
            }

            if (!SkipBinaryIntro(priv, ref j))
            {
                i = j;
                continue;
            }

            if (j + len <= priv.Length && name.Length > 0)
            {
                map[name] = DecryptCharString(priv, j, len, lenIV);
            }

            i = j + len;
        }

        return map;
    }

    // Skips the "RD " / "-| " operator and the single separating space, leaving the cursor at the binary.
    private static bool SkipBinaryIntro(byte[] data, ref int i)
    {
        while (i < data.Length && IsWhite(data[i]))
        {
            i++;
        }

        // operator token (RD or -|)
        while (i < data.Length && !IsWhite(data[i]))
        {
            i++;
        }

        if (i >= data.Length)
        {
            return false;
        }

        // exactly one separating space precedes the binary
        i++;
        return true;
    }

    private static bool ReadIntAt(byte[] data, ref int i, out int value)
    {
        value = 0;
        while (i < data.Length && IsWhite(data[i]))
        {
            i++;
        }

        int start = i;
        bool neg = false;
        if (i < data.Length && (data[i] == '-' || data[i] == '+'))
        {
            neg = data[i] == '-';
            i++;
        }

        long v = 0;
        int digits = 0;
        while (i < data.Length && data[i] >= '0' && data[i] <= '9')
        {
            v = (v * 10) + (data[i] - '0');
            i++;
            digits++;
        }

        if (digits == 0)
        {
            i = start;
            return false;
        }

        value = (int)(neg ? -v : v);
        return true;
    }

    // ---- helpers --------------------------------------------------------

    private static bool IsWhite(byte b) => b == ' ' || b == '\t' || b == '\r' || b == '\n' || b == '\f' || b == 0;

    private static bool IsHex(byte b) => (b >= '0' && b <= '9') || (b >= 'a' && b <= 'f') || (b >= 'A' && b <= 'F');

    private static int HexVal(byte b) => b <= '9' ? b - '0' : (b <= 'F' ? b - 'A' + 10 : b - 'a' + 10);

    private static string Ascii(byte[] data, int start, int len)
    {
        var sb = new StringBuilder(len);
        for (int i = 0; i < len && start + i < data.Length; i++)
        {
            sb.Append((char)data[start + i]);
        }

        return sb.ToString();
    }

    private static int IndexOf(byte[] data, string needle, int from)
    {
        int n = needle.Length;
        for (int i = Math.Max(0, from); i <= data.Length - n; i++)
        {
            int k = 0;
            while (k < n && data[i + k] == (byte)needle[k])
            {
                k++;
            }

            if (k == n)
            {
                return i;
            }
        }

        return -1;
    }

    // ---- Type 1 charstring interpreter ----------------------------------

    private sealed class Type1Interpreter
    {
        private readonly Type1Font _font;
        private readonly List<double> _st = new List<double>();
        private readonly List<double> _ps = new List<double>();
        private readonly List<double> _flex = new List<double>(); // pairs x,y
        private double _x;
        private double _y;
        private double _sbx;
        private bool _inFlex;
        private bool _done;
        private int _depth;

        public Type1Interpreter(Type1Font font) => _font = font;

        public ContourBuilder Builder { get; } = new ContourBuilder();

        public double Width { get; private set; }

        public void Run(byte[] cs) => Exec(cs, 0, 0);

        private void Exec(byte[] cs, double offsetX, double offsetY)
        {
            if (_depth++ > 30 || cs is null)
            {
                _depth--;
                return;
            }

            int i = 0;
            while (i < cs.Length && !_done)
            {
                int b = cs[i++];
                if (b >= 32)
                {
                    _st.Add(ReadNumber(cs, ref i, b));
                    continue;
                }

                switch (b)
                {
                    case 1: // hstem
                    case 3: // vstem
                        _st.Clear();
                        break;
                    case 4: // vmoveto
                        MoveBy(0, Last(0));
                        _st.Clear();
                        break;
                    case 5: // rlineto
                        LineBy(Nth(0), Nth(1));
                        _st.Clear();
                        break;
                    case 6: // hlineto
                        LineBy(Last(0), 0);
                        _st.Clear();
                        break;
                    case 7: // vlineto
                        LineBy(0, Last(0));
                        _st.Clear();
                        break;
                    case 8: // rrcurveto
                        CurveBy(Nth(0), Nth(1), Nth(2), Nth(3), Nth(4), Nth(5));
                        _st.Clear();
                        break;
                    case 9: // closepath
                        _st.Clear();
                        break;
                    case 10: // callsubr
                        CallSubr();
                        break;
                    case 11: // return
                        _depth--;
                        return;
                    case 13: // hsbw
                        _sbx = Nth(0);
                        Width = Nth(1);
                        _x = _sbx + offsetX;
                        _y = offsetY;
                        _st.Clear();
                        break;
                    case 14: // endchar
                        _done = true;
                        break;
                    case 21: // rmoveto
                        MoveBy(Nth(0), Nth(1));
                        _st.Clear();
                        break;
                    case 22: // hmoveto
                        MoveBy(Last(0), 0);
                        _st.Clear();
                        break;
                    case 30: // vhcurveto
                        CurveBy(0, Nth(0), Nth(1), Nth(2), Nth(3), 0);
                        _st.Clear();
                        break;
                    case 31: // hvcurveto
                        CurveBy(Nth(0), 0, Nth(1), Nth(2), 0, Nth(3));
                        _st.Clear();
                        break;
                    case 12:
                        ExecEscape(cs[i++], offsetX, offsetY);
                        break;
                    default:
                        _st.Clear();
                        break;
                }
            }

            _depth--;
        }

        private void ExecEscape(int b2, double offsetX, double offsetY)
        {
            switch (b2)
            {
                case 0: // dotsection
                    _st.Clear();
                    break;
                case 1: // vstem3
                case 2: // hstem3
                    _st.Clear();
                    break;
                case 6: // seac
                    Seac(offsetX, offsetY);
                    break;
                case 7: // sbw
                    _sbx = Nth(0);
                    Width = Nth(2);
                    _x = _sbx + offsetX;
                    _y = Nth(1) + offsetY;
                    _st.Clear();
                    break;
                case 12: // div
                    Div();
                    break;
                case 16: // callothersubr
                    CallOtherSubr();
                    break;
                case 17: // pop
                    _st.Add(_ps.Count > 0 ? PopPs() : 0);
                    break;
                case 33: // setcurrentpoint
                    if (_st.Count >= 2)
                    {
                        _x = _st[0] + offsetX;
                        _y = _st[1] + offsetY;
                    }

                    _st.Clear();
                    break;
                default:
                    _st.Clear();
                    break;
            }
        }

        private void Seac(double offsetX, double offsetY)
        {
            if (_st.Count < 5)
            {
                _st.Clear();
                return;
            }

            double asb = _st[0];
            double adx = _st[1];
            double ady = _st[2];
            int bchar = (int)_st[3];
            int achar = (int)_st[4];
            _st.Clear();

            string? baseName = StandardEncoding.Name(bchar);
            string? accentName = StandardEncoding.Name(achar);
            if (baseName != null && _font.TryCharString(baseName, out byte[] baseCs))
            {
                Exec(baseCs, offsetX, offsetY);
            }

            _done = false; // base endchar must not terminate the composite
            if (accentName != null && _font.TryCharString(accentName, out byte[] accCs))
            {
                double dx = _sbx - asb + adx + offsetX;
                Exec(accCs, dx, ady + offsetY);
            }

            _done = true;
        }

        private void CallSubr()
        {
            if (_st.Count == 0)
            {
                return;
            }

            int idx = (int)PopSt();
            byte[]? sub = _font.Subr(idx);
            if (sub is { Length: > 0 })
            {
                Exec(sub, 0, 0);
            }
        }

        private void CallOtherSubr()
        {
            if (_st.Count < 2)
            {
                _st.Clear();
                return;
            }

            int othersubr = (int)PopSt();
            int n = (int)PopSt();
            var args = new List<double>(n);
            for (int k = 0; k < n && _st.Count > 0; k++)
            {
                args.Insert(0, PopSt());
            }

            switch (othersubr)
            {
                case 1: // start flex
                    _inFlex = true;
                    _flex.Clear();
                    break;
                case 2: // collect flex point (handled in rmoveto)
                    break;
                case 0: // end flex
                    EndFlex(args);
                    break;
                case 3: // hint replacement -> push subr# for the following pop/callsubr
                    _ps.Add(args.Count > 0 ? args[0] : 3);
                    break;
                default:
                    // Unknown: leave args available for subsequent pops (reverse order).
                    for (int k = args.Count - 1; k >= 0; k--)
                    {
                        _ps.Add(args[k]);
                    }

                    break;
            }
        }

        private void EndFlex(List<double> args)
        {
            _inFlex = false;
            if (_flex.Count >= 14)
            {
                // _flex holds 7 points; [0] is the reference, [1..3] first curve, [4..6] second.
                Builder.CubicTo(_flex[2], _flex[3], _flex[4], _flex[5], _flex[6], _flex[7]);
                Builder.CubicTo(_flex[8], _flex[9], _flex[10], _flex[11], _flex[12], _flex[13]);
                _x = _flex[12];
                _y = _flex[13];
            }

            // Leave end x,y for the two pops that follow (args = [flexheight, x, y]).
            if (args.Count >= 3)
            {
                _ps.Add(args[2]);
                _ps.Add(args[1]);
            }
        }

        private void Div()
        {
            if (_st.Count < 2)
            {
                return;
            }

            double bb = PopSt();
            double aa = PopSt();
            _st.Add(bb != 0 ? aa / bb : 0);
        }

        private void MoveBy(double dx, double dy)
        {
            _x += dx;
            _y += dy;
            if (_inFlex)
            {
                _flex.Add(_x);
                _flex.Add(_y);
            }
            else
            {
                Builder.MoveTo(_x, _y);
            }
        }

        private void LineBy(double dx, double dy)
        {
            _x += dx;
            _y += dy;
            Builder.LineTo(_x, _y);
        }

        private void CurveBy(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
        {
            double c1x = _x + dx1, c1y = _y + dy1;
            double c2x = c1x + dx2, c2y = c1y + dy2;
            _x = c2x + dx3;
            _y = c2y + dy3;
            Builder.CubicTo(c1x, c1y, c2x, c2y, _x, _y);
        }

        // Stack helpers: Nth indexes from the bottom of the current operator's operands.
        private double Nth(int i) => i < _st.Count ? _st[i] : 0;

        private double Last(int fromEnd) => _st.Count - 1 - fromEnd >= 0 ? _st[_st.Count - 1 - fromEnd] : 0;

        private double PopSt()
        {
            double v = _st[_st.Count - 1];
            _st.RemoveAt(_st.Count - 1);
            return v;
        }

        private double PopPs()
        {
            double v = _ps[_ps.Count - 1];
            _ps.RemoveAt(_ps.Count - 1);
            return v;
        }

        private static double ReadNumber(byte[] cs, ref int i, int b)
        {
            if (b <= 246)
            {
                return b - 139;
            }

            if (b <= 250)
            {
                return ((b - 247) * 256) + cs[i++] + 108;
            }

            if (b <= 254)
            {
                return -((b - 251) * 256) - cs[i++] - 108;
            }

            // 255: 32-bit signed integer (Type 1 — not 16.16 fixed).
            int v = (cs[i] << 24) | (cs[i + 1] << 16) | (cs[i + 2] << 8) | cs[i + 3];
            i += 4;
            return v;
        }
    }
}
