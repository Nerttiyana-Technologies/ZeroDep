using System;
using System.Collections.Generic;
using System.Text;
using ZeroDep.Lexing;
using ZeroDep.Objects;

namespace ZeroDep.Content;

/// <summary>One decoded glyph: its Unicode text, advance width (in 1000-unit em), and whether it is a space.</summary>
internal readonly struct Glyph
{
    public Glyph(string text, double widthEm, bool isSpace)
    {
        Text = text;
        WidthEm = widthEm;
        IsSpace = isSpace;
    }

    public string Text { get; }
    public double WidthEm { get; }
    public bool IsSpace { get; }
}

/// <summary>
/// Decodes a font's shown-text bytes to Unicode and supplies glyph advance widths.
/// Uses /ToUnicode when present (simple and Type0), simple /Widths and Type0 /W,/DW for advances,
/// and a WinAnsi fallback for simple fonts without a ToUnicode map. ISO 32000-2 §9.6–9.10.
/// </summary>
internal sealed class FontInfo
{
    private readonly int _codeBytes;
    private readonly Dictionary<int, string>? _toUnicode;
    private readonly double[] _simpleWidths;
    private readonly int _firstChar;
    private readonly Dictionary<int, double>? _cidWidths;
    private readonly Dictionary<int, string>? _differences;
    private readonly double _defaultWidth;

    public FontInfo(PdfDictionary dict, Func<PdfObject, PdfObject> resolve, Func<PdfStream, byte[]> decode)
    {
        bool isType0 = (dict["Subtype"] as PdfName)?.Value == "Type0";
        _codeBytes = isType0 ? 2 : 1;

        if (resolve(dict["ToUnicode"] ?? PdfNull.Instance) is PdfStream toUnicodeStream)
        {
            try { _toUnicode = ToUnicodeCMap.Parse(decode(toUnicodeStream)); }
            catch { _toUnicode = null; }
        }

        if (isType0)
        {
            _defaultWidth = 1000;
            _simpleWidths = Array.Empty<double>();
            if (resolve(dict["DescendantFonts"] ?? PdfNull.Instance) is PdfArray descendants && descendants.Count > 0
                && resolve(descendants[0]) is PdfDictionary cidFont)
            {
                if (resolve(cidFont["DW"] ?? PdfNull.Instance) is PdfNumber dw) _defaultWidth = dw.AsDouble;
                if (resolve(cidFont["W"] ?? PdfNull.Instance) is PdfArray w) _cidWidths = ParseCidWidths(resolve, w);
            }
        }
        else
        {
            _defaultWidth = 500;
            _firstChar = resolve(dict["FirstChar"] ?? PdfNull.Instance) is PdfNumber fc ? (int)fc.AsInt64 : 0;
            if (resolve(dict["Widths"] ?? PdfNull.Instance) is PdfArray widths)
            {
                _simpleWidths = new double[widths.Count];
                for (int i = 0; i < widths.Count; i++)
                {
                    _simpleWidths[i] = resolve(widths[i]) is PdfNumber n ? n.AsDouble : 0;
                }
            }
            else
            {
                _simpleWidths = Array.Empty<double>();
            }

            // /Encoding /Differences (used when there is no /ToUnicode map).
            if (_toUnicode is null && resolve(dict["Encoding"] ?? PdfNull.Instance) is PdfDictionary encoding
                && resolve(encoding["Differences"] ?? PdfNull.Instance) is PdfArray differences)
            {
                _differences = new Dictionary<int, string>();
                int code = 0;
                foreach (PdfObject item in differences.Items)
                {
                    if (item is PdfInteger n) code = (int)n.Value;
                    else if (item is PdfName name) _differences[code++] = GlyphList.ToUnicode(name.Value);
                }
            }
        }
    }

    /// <summary>Decodes a shown-text byte string into glyphs.</summary>
    public List<Glyph> Decode(byte[] bytes)
    {
        var glyphs = new List<Glyph>();
        if (_codeBytes == 1)
        {
            foreach (byte b in bytes)
            {
                string? text = Lookup(b);
                if (text is null && _differences is not null && _differences.TryGetValue(b, out string? diff)) text = diff;
                text ??= WinAnsiString(b);
                glyphs.Add(new Glyph(text, WidthOf(b), b == 32));
            }
        }
        else
        {
            for (int i = 0; i + 1 < bytes.Length; i += 2)
            {
                int code = (bytes[i] << 8) | bytes[i + 1];
                glyphs.Add(new Glyph(Lookup(code) ?? string.Empty, WidthOf(code), false));
            }
        }
        return glyphs;
    }

    private string? Lookup(int code)
        => _toUnicode is not null && _toUnicode.TryGetValue(code, out string? s) ? s : null;

    private double WidthOf(int code)
    {
        if (_codeBytes == 1)
        {
            int idx = code - _firstChar;
            if (idx >= 0 && idx < _simpleWidths.Length && _simpleWidths[idx] > 0) return _simpleWidths[idx];
            return _defaultWidth;
        }
        if (_cidWidths is not null && _cidWidths.TryGetValue(code, out double w)) return w;
        return _defaultWidth;
    }

    private static Dictionary<int, double> ParseCidWidths(Func<PdfObject, PdfObject> resolve, PdfArray w)
    {
        var map = new Dictionary<int, double>();
        int i = 0;
        while (i < w.Count)
        {
            if (resolve(w[i]) is not PdfNumber first) { i++; continue; }
            if (i + 1 >= w.Count) break;

            PdfObject next = resolve(w[i + 1]);
            if (next is PdfArray array)
            {
                int c = (int)first.AsInt64;
                for (int j = 0; j < array.Count; j++)
                {
                    if (resolve(array[j]) is PdfNumber wn) map[c + j] = wn.AsDouble;
                }
                i += 2;
            }
            else if (next is PdfNumber last && i + 2 < w.Count && resolve(w[i + 2]) is PdfNumber value)
            {
                for (int c = (int)first.AsInt64; c <= (int)last.AsInt64 && c - (int)first.AsInt64 < 70000; c++)
                {
                    map[c] = value.AsDouble;
                }
                i += 3;
            }
            else
            {
                i++;
            }
        }
        return map;
    }

    private static string WinAnsiString(int code)
    {
        char c = WinAnsiChar(code);
        return c == '\0' ? string.Empty : c.ToString();
    }

    private static char WinAnsiChar(int code)
    {
        if (code >= 0x20 && code <= 0x7E) return (char)code;
        if (code >= 0xA0 && code <= 0xFF) return (char)code;
        switch (code)
        {
            case 0x85: return '…';
            case 0x91: return '‘';
            case 0x92: return '’';
            case 0x93: return '“';
            case 0x94: return '”';
            case 0x95: return '•';
            case 0x96: return '–';
            case 0x97: return '—';
            case 0x99: return '™';
            default: return '\0';
        }
    }
}

/// <summary>Parses a /ToUnicode CMap (bfchar/bfrange) into a code → Unicode map (ISO 32000-2 §9.10.3).</summary>
internal static class ToUnicodeCMap
{
    public static Dictionary<int, string> Parse(byte[] data)
    {
        var map = new Dictionary<int, string>();
        var lexer = new PdfLexer(data, 0, data.Length);
        while (true)
        {
            Token token = lexer.Next();
            if (token.Type == TokenType.Eof) break;
            if (token.Type != TokenType.Keyword) continue;
            if (token.Text == "beginbfchar") ParseBfChar(lexer, map);
            else if (token.Text == "beginbfrange") ParseBfRange(lexer, map);
        }
        return map;
    }

    private static void ParseBfChar(PdfLexer lexer, Dictionary<int, string> map)
    {
        while (true)
        {
            Token src = lexer.Next();
            if (src.Type != TokenType.HexString) break;
            Token dst = lexer.Next();
            if (dst.Type != TokenType.HexString) break;
            map[ToInt(src.Bytes!)] = Utf16(dst.Bytes!);
        }
    }

    private static void ParseBfRange(PdfLexer lexer, Dictionary<int, string> map)
    {
        while (true)
        {
            Token lo = lexer.Next();
            if (lo.Type != TokenType.HexString) break;
            Token hi = lexer.Next();
            if (hi.Type != TokenType.HexString) break;
            Token dst = lexer.Next();

            int loCode = ToInt(lo.Bytes!);
            int hiCode = ToInt(hi.Bytes!);
            if (dst.Type == TokenType.HexString)
            {
                for (int c = loCode; c <= hiCode && c - loCode < 65536; c++)
                {
                    map[c] = Utf16Increment(dst.Bytes!, c - loCode);
                }
            }
            else if (dst.Type == TokenType.ArrayStart)
            {
                int c = loCode;
                while (true)
                {
                    Token item = lexer.Next();
                    if (item.Type == TokenType.ArrayEnd || item.Type == TokenType.Eof) break;
                    if (item.Type == TokenType.HexString) map[c++] = Utf16(item.Bytes!);
                }
            }
            else
            {
                break;
            }
        }
    }

    private static int ToInt(byte[] bytes)
    {
        int v = 0;
        foreach (byte b in bytes) v = (v << 8) | b;
        return v;
    }

    private static string Utf16(byte[] bytes)
        => bytes.Length >= 2 ? Encoding.BigEndianUnicode.GetString(bytes, 0, bytes.Length & ~1) : string.Empty;

    private static string Utf16Increment(byte[] baseBytes, int offset)
    {
        if (baseBytes.Length < 2) return Utf16(baseBytes);
        var copy = (byte[])baseBytes.Clone();
        int last = (copy[copy.Length - 2] << 8) | copy[copy.Length - 1];
        last += offset;
        copy[copy.Length - 2] = (byte)((last >> 8) & 0xFF);
        copy[copy.Length - 1] = (byte)(last & 0xFF);
        return Utf16(copy);
    }
}
