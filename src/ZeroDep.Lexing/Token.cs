namespace ZeroDep.Lexing;

/// <summary>A single lexed token with its decoded payload.</summary>
internal readonly struct Token
{
    private Token(TokenType type, string? text, byte[]? bytes, long intValue, double realValue)
    {
        Type = type;
        Text = text;
        Bytes = bytes;
        IntValue = intValue;
        RealValue = realValue;
    }

    /// <summary>The token category.</summary>
    public TokenType Type { get; }

    /// <summary>Decoded text for <see cref="TokenType.Name"/> and <see cref="TokenType.Keyword"/>; otherwise null.</summary>
    public string? Text { get; }

    /// <summary>Decoded raw bytes for string tokens; otherwise null.</summary>
    public byte[]? Bytes { get; }

    /// <summary>Value of an <see cref="TokenType.Integer"/> token.</summary>
    public long IntValue { get; }

    /// <summary>Value of a <see cref="TokenType.Real"/> token.</summary>
    public double RealValue { get; }

    public static Token Eof { get; } = new Token(TokenType.Eof, null, null, 0, 0);

    public static Token Delimiter(TokenType type) => new Token(type, null, null, 0, 0);

    public static Token Keyword(string text) => new Token(TokenType.Keyword, text, null, 0, 0);

    public static Token Name(string text) => new Token(TokenType.Name, text, null, 0, 0);

    public static Token Integer(long value) => new Token(TokenType.Integer, null, null, value, value);

    public static Token Real(double value) => new Token(TokenType.Real, null, null, 0, value);

    public static Token String(byte[] bytes, bool hex)
        => new Token(hex ? TokenType.HexString : TokenType.LiteralString, null, bytes, 0, 0);
}
