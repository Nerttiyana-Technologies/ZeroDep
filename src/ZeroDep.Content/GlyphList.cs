using System.Collections.Generic;
using System.Globalization;

namespace ZeroDep.Content;

/// <summary>
/// Maps PostScript glyph names to Unicode for <c>/Differences</c> encodings (a focused Adobe Glyph
/// List subset plus the <c>uniXXXX</c> / <c>uXXXXXX</c> conventions). ISO 32000-2 §9.6.6, §D.
/// </summary>
internal static class GlyphList
{
    private static readonly Dictionary<string, string> Names = Build();

    /// <summary>Resolves a glyph name to its Unicode string, or empty if unknown.</summary>
    public static string ToUnicode(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        if (Names.TryGetValue(name, out string? mapped)) return mapped;

        // uniXXXX (one or more 4-hex code units)
        if (name.Length >= 7 && name.StartsWith("uni", System.StringComparison.Ordinal) && (name.Length - 3) % 4 == 0)
        {
            var sb = new System.Text.StringBuilder();
            bool ok = true;
            for (int i = 3; i < name.Length; i += 4)
            {
                if (int.TryParse(name.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                    sb.Append((char)code);
                else { ok = false; break; }
            }
            if (ok && sb.Length > 0) return sb.ToString();
        }

        // uXXXX..XXXXXX (a single code point, 4–6 hex digits)
        if (name.Length >= 5 && name.Length <= 7 && name[0] == 'u'
            && int.TryParse(name.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp)
            && cp >= 0 && cp <= 0x10FFFF)
        {
            return char.ConvertFromUtf32(cp);
        }

        // Single-character names map to themselves (e.g. "A").
        if (name.Length == 1) return name;
        return string.Empty;
    }

    private static Dictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(System.StringComparer.Ordinal);
        void Add(string n, char c) => map[n] = c.ToString();

        for (char c = 'A'; c <= 'Z'; c++) Add(c.ToString(), c);
        for (char c = 'a'; c <= 'z'; c++) Add(c.ToString(), c);

        string[] digits = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };
        for (int i = 0; i < digits.Length; i++) Add(digits[i], (char)('0' + i));

        Add("space", ' ');
        Add("exclam", '!');
        Add("quotedbl", '"');
        Add("numbersign", '#');
        Add("dollar", '$');
        Add("percent", '%');
        Add("ampersand", '&');
        Add("quotesingle", '\'');
        Add("parenleft", '(');
        Add("parenright", ')');
        Add("asterisk", '*');
        Add("plus", '+');
        Add("comma", ',');
        Add("hyphen", '-');
        Add("period", '.');
        Add("slash", '/');
        Add("colon", ':');
        Add("semicolon", ';');
        Add("less", '<');
        Add("equal", '=');
        Add("greater", '>');
        Add("question", '?');
        Add("at", '@');
        Add("bracketleft", '[');
        Add("backslash", '\\');
        Add("bracketright", ']');
        Add("asciicircum", '^');
        Add("underscore", '_');
        Add("grave", '`');
        Add("braceleft", '{');
        Add("bar", '|');
        Add("braceright", '}');
        Add("asciitilde", '~');
        Add("quoteleft", '‘');
        Add("quoteright", '’');
        Add("quotedblleft", '“');
        Add("quotedblright", '”');
        Add("endash", '–');
        Add("emdash", '—');
        Add("bullet", '•');
        Add("ellipsis", '…');
        Add("trademark", '™');
        Add("copyright", '©');
        Add("registered", '®');
        Add("degree", '°');
        Add("nbspace", ' ');
        return map;
    }
}
