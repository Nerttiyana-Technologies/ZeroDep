namespace ZeroDep.IO;

/// <summary>
/// Classifies raw bytes according to the PDF syntax categories
/// (white-space, delimiter, regular), per ISO 32000-2 §7.2.
/// </summary>
internal static class ByteClass
{
    /// <summary>PDF white-space: NUL, TAB, LF, FF, CR, and SPACE.</summary>
    public static bool IsWhitespace(int b)
        => b == 0x00 || b == 0x09 || b == 0x0A || b == 0x0C || b == 0x0D || b == 0x20;

    /// <summary>PDF delimiters: <c>( ) &lt; &gt; [ ] { } / %</c>.</summary>
    public static bool IsDelimiter(int b)
        => b == '(' || b == ')' || b == '<' || b == '>' || b == '['
        || b == ']' || b == '{' || b == '}' || b == '/' || b == '%';

    /// <summary>A "regular" character: any byte that is neither white-space nor a delimiter.</summary>
    public static bool IsRegular(int b) => b >= 0 && !IsWhitespace(b) && !IsDelimiter(b);

    /// <summary>ASCII decimal digit.</summary>
    public static bool IsDigit(int b) => b >= '0' && b <= '9';

    /// <summary>End-of-line marker byte (CR or LF), in any platform combination.</summary>
    public static bool IsEol(int b) => b == 0x0A || b == 0x0D;
}
