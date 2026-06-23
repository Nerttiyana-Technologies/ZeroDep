namespace ZeroDep.Lexing;

/// <summary>The category of a lexed PDF token.</summary>
internal enum TokenType
{
    /// <summary>End of the buffer.</summary>
    Eof,
    /// <summary>An integer literal.</summary>
    Integer,
    /// <summary>A real (floating-point) literal.</summary>
    Real,
    /// <summary>A name object, e.g. <c>/Type</c> (the leading slash is stripped).</summary>
    Name,
    /// <summary>A literal string in parentheses, e.g. <c>(text)</c>.</summary>
    LiteralString,
    /// <summary>A hexadecimal string in angle brackets, e.g. <c>&lt;48656C&gt;</c>.</summary>
    HexString,
    /// <summary>The <c>[</c> array-open delimiter.</summary>
    ArrayStart,
    /// <summary>The <c>]</c> array-close delimiter.</summary>
    ArrayEnd,
    /// <summary>The <c>&lt;&lt;</c> dictionary-open delimiter.</summary>
    DictStart,
    /// <summary>The <c>&gt;&gt;</c> dictionary-close delimiter.</summary>
    DictEnd,
    /// <summary>A bare keyword such as <c>obj</c>, <c>stream</c>, <c>R</c>, <c>true</c>, <c>null</c>.</summary>
    Keyword,
}
