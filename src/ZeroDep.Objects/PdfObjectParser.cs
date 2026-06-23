using System;
using System.Collections.Generic;
using System.Text;
using ZeroDep.Abstractions;
using ZeroDep.Lexing;

namespace ZeroDep.Objects;

/// <summary>
/// Builds <see cref="PdfObject"/> values from a <see cref="PdfLexer"/> token stream,
/// including indirect references, dictionaries, arrays, and streams.
/// </summary>
internal sealed class PdfObjectParser
{
    private static readonly byte[] EndStreamKeyword = Encoding.ASCII.GetBytes("endstream");

    private readonly PdfLexer _lexer;

    public PdfObjectParser(PdfLexer lexer)
        => _lexer = lexer ?? throw new ArgumentNullException(nameof(lexer));

    /// <summary>Creates a parser over the bytes in <paramref name="buffer"/> between <paramref name="start"/> and <paramref name="end"/>.</summary>
    public PdfObjectParser(byte[] buffer, int start, int end)
        : this(new PdfLexer(buffer, start, end))
    {
    }

    /// <summary>Parses the next value from the token stream.</summary>
    public PdfObject ParseValue() => FromToken(_lexer.Next());

    private PdfObject FromToken(Token token)
    {
        switch (token.Type)
        {
            case TokenType.Integer:
                return IntegerOrReference(token.IntValue);
            case TokenType.Real:
                return new PdfReal(token.RealValue);
            case TokenType.Name:
                return new PdfName(token.Text!);
            case TokenType.LiteralString:
                return new PdfString(token.Bytes!, isHexString: false);
            case TokenType.HexString:
                return new PdfString(token.Bytes!, isHexString: true);
            case TokenType.ArrayStart:
                return ParseArray();
            case TokenType.DictStart:
                return ParseDictionaryOrStream();
            case TokenType.Keyword:
                return token.Text switch
                {
                    "true" => PdfBoolean.True,
                    "false" => PdfBoolean.False,
                    "null" => PdfNull.Instance,
                    _ => throw new PdfSyntaxException($"Unexpected keyword '{token.Text}'."),
                };
            case TokenType.Eof:
                throw new PdfSyntaxException("Unexpected end of input while parsing an object.");
            default:
                throw new PdfSyntaxException($"Unexpected token '{token.Type}'.");
        }
    }

    private PdfObject IntegerOrReference(long first)
    {
        int mark = _lexer.Position;
        Token second = _lexer.Next();
        if (second.Type == TokenType.Integer)
        {
            Token third = _lexer.Next();
            if (third.Type == TokenType.Keyword && third.Text == "R")
            {
                return new PdfReference((int)first, (int)second.IntValue);
            }
        }

        _lexer.Seek(mark);
        return new PdfInteger(first);
    }

    private PdfArray ParseArray()
    {
        var items = new List<PdfObject>();
        while (true)
        {
            Token token = _lexer.Next();
            if (token.Type == TokenType.ArrayEnd) break;
            if (token.Type == TokenType.Eof) throw new PdfSyntaxException("Unterminated array.");
            items.Add(FromToken(token));
        }
        return new PdfArray(items);
    }

    private PdfObject ParseDictionaryOrStream()
    {
        var entries = new Dictionary<string, PdfObject>(StringComparer.Ordinal);
        while (true)
        {
            Token keyToken = _lexer.Next();
            if (keyToken.Type == TokenType.DictEnd) break;
            if (keyToken.Type == TokenType.Eof) throw new PdfSyntaxException("Unterminated dictionary.");
            if (keyToken.Type != TokenType.Name) throw new PdfSyntaxException("Dictionary key must be a name.");

            string key = keyToken.Text!;
            Token valueToken = _lexer.Next();
            if (valueToken.Type == TokenType.DictEnd) throw new PdfSyntaxException($"Missing value for /{key}.");
            entries[key] = FromToken(valueToken);
        }

        var dictionary = new PdfDictionary(entries);

        int mark = _lexer.Position;
        Token maybeStream = _lexer.Next();
        if (maybeStream.Type == TokenType.Keyword && maybeStream.Text == "stream")
        {
            return ParseStream(dictionary);
        }

        _lexer.Seek(mark);
        return dictionary;
    }

    private PdfStream ParseStream(PdfDictionary dictionary)
    {
        byte[] buffer = _lexer.Buffer;
        int end = _lexer.End;
        int pos = _lexer.Position; // immediately after the "stream" keyword

        // The "stream" keyword is followed by CRLF or LF (a lone CR is tolerated).
        if (pos < end && buffer[pos] == 0x0D)
        {
            pos++;
            if (pos < end && buffer[pos] == 0x0A) pos++;
        }
        else if (pos < end && buffer[pos] == 0x0A)
        {
            pos++;
        }

        int dataStart = pos;
        int dataEnd = DetermineStreamEnd(dictionary, buffer, dataStart, end);
        if (dataEnd < dataStart) dataEnd = dataStart;

        var raw = new byte[dataEnd - dataStart];
        Array.Copy(buffer, dataStart, raw, 0, raw.Length);

        int endStream = IndexOf(buffer, dataEnd, end, EndStreamKeyword);
        _lexer.Seek(endStream >= 0 ? endStream + EndStreamKeyword.Length : end);
        return new PdfStream(dictionary, raw);
    }

    private static int DetermineStreamEnd(PdfDictionary dictionary, byte[] buffer, int dataStart, int end)
    {
        if (dictionary.TryGetValue("Length", out var lengthObj)
            && lengthObj is PdfInteger length
            && length.Value >= 0
            && dataStart + length.Value <= end)
        {
            int declaredEnd = dataStart + (int)length.Value;
            // Trust the declared length only if "endstream" follows within a small window.
            int window = Math.Min(end, declaredEnd + 16);
            if (IndexOf(buffer, declaredEnd, window, EndStreamKeyword) >= 0)
            {
                return declaredEnd;
            }
        }

        return ScanToEndStream(buffer, dataStart, end);
    }

    private static int ScanToEndStream(byte[] buffer, int dataStart, int end)
    {
        int idx = IndexOf(buffer, dataStart, end, EndStreamKeyword);
        if (idx < 0) return end;

        int trimmed = idx;
        if (trimmed - 1 >= dataStart && buffer[trimmed - 1] == 0x0A)
        {
            trimmed--;
            if (trimmed - 1 >= dataStart && buffer[trimmed - 1] == 0x0D) trimmed--;
        }
        else if (trimmed - 1 >= dataStart && buffer[trimmed - 1] == 0x0D)
        {
            trimmed--;
        }
        return trimmed;
    }

    private static int IndexOf(byte[] buffer, int start, int end, byte[] pattern)
    {
        int last = end - pattern.Length;
        for (int i = Math.Max(0, start); i <= last; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (buffer[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }
}
