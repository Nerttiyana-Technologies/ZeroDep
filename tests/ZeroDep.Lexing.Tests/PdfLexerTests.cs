using System.Text;
using Xunit;
using ZeroDep.Lexing;

namespace ZeroDep.Lexing.Tests;

public sealed class PdfLexerTests
{
    private static PdfLexer Lex(string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        return new PdfLexer(bytes, 0, bytes.Length);
    }

    [Fact]
    public void TokenizesDictionaryWithNamesAndInteger()
    {
        PdfLexer lexer = Lex("<< /Type /Page /Count 3 >>");

        Assert.Equal(TokenType.DictStart, lexer.Next().Type);

        Token type = lexer.Next();
        Assert.Equal(TokenType.Name, type.Type);
        Assert.Equal("Type", type.Text);

        Assert.Equal("Page", lexer.Next().Text);

        Token count = lexer.Next();
        Assert.Equal("Count", count.Text);

        Token three = lexer.Next();
        Assert.Equal(TokenType.Integer, three.Type);
        Assert.Equal(3L, three.IntValue);

        Assert.Equal(TokenType.DictEnd, lexer.Next().Type);
        Assert.Equal(TokenType.Eof, lexer.Next().Type);
    }

    [Fact]
    public void ParsesIntegersAndReals()
    {
        PdfLexer lexer = Lex("3 -4 5.5 +6 -.25");

        Assert.Equal(3L, lexer.Next().IntValue);
        Assert.Equal(-4L, lexer.Next().IntValue);

        Token real = lexer.Next();
        Assert.Equal(TokenType.Real, real.Type);
        Assert.Equal(5.5, real.RealValue, 5);

        Assert.Equal(6L, lexer.Next().IntValue);

        Token negFraction = lexer.Next();
        Assert.Equal(TokenType.Real, negFraction.Type);
        Assert.Equal(-0.25, negFraction.RealValue, 5);
    }

    [Fact]
    public void DecodesHashEscapesInNames()
    {
        Token name = Lex("/A#42C").Next();
        Assert.Equal(TokenType.Name, name.Type);
        Assert.Equal("ABC", name.Text);
    }

    [Fact]
    public void ReadsLiteralAndHexStrings()
    {
        Token literal = Lex("(Hello)").Next();
        Assert.Equal(TokenType.LiteralString, literal.Type);
        Assert.Equal("Hello", Encoding.ASCII.GetString(literal.Bytes!));

        Token hex = Lex("<48656C6C6F>").Next();
        Assert.Equal(TokenType.HexString, hex.Type);
        Assert.Equal("Hello", Encoding.ASCII.GetString(hex.Bytes!));
    }

    [Fact]
    public void HandlesEscapesAndNestedParens()
    {
        Token token = Lex("(a\\(b\\)\\n)").Next();
        byte[] expected = { (byte)'a', (byte)'(', (byte)'b', (byte)')', 0x0A };
        Assert.Equal(expected, token.Bytes);
    }

    [Fact]
    public void SkipsCommentsAndWhitespace()
    {
        PdfLexer lexer = Lex("% a comment\n  42");
        Token token = lexer.Next();
        Assert.Equal(TokenType.Integer, token.Type);
        Assert.Equal(42L, token.IntValue);
    }
}
