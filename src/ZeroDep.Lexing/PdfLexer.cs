using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ZeroDep.IO;

namespace ZeroDep.Lexing;

/// <summary>
/// Tokenizes a region of PDF bytes into <see cref="Token"/> values (ISO 32000-2 §7.2–7.3).
/// Stateless apart from a cursor; supports <see cref="Position"/>/<see cref="Seek"/> for lookahead.
/// </summary>
internal sealed class PdfLexer
{
    private readonly byte[] _buffer;
    private readonly int _end;
    private int _pos;

    /// <summary>Creates a lexer over <paramref name="buffer"/> between <paramref name="start"/> and <paramref name="end"/>.</summary>
    public PdfLexer(byte[] buffer, int start, int end)
    {
        _buffer = buffer;
        _pos = start;
        _end = end;
    }

    /// <summary>The underlying buffer (used by the parser for raw stream extraction).</summary>
    public byte[] Buffer => _buffer;

    /// <summary>The exclusive end offset of the lexed region.</summary>
    public int End => _end;

    /// <summary>The current read cursor.</summary>
    public int Position => _pos;

    /// <summary>Moves the read cursor to <paramref name="position"/> (used for lookahead rollback).</summary>
    public void Seek(int position) => _pos = position;

    /// <summary>Reads and returns the next token, advancing the cursor.</summary>
    public Token Next()
    {
        SkipWhitespaceAndComments();
        if (_pos >= _end) return Token.Eof;

        int b = _buffer[_pos];
        switch (b)
        {
            case (byte)'[':
                _pos++;
                return Token.Delimiter(TokenType.ArrayStart);
            case (byte)']':
                _pos++;
                return Token.Delimiter(TokenType.ArrayEnd);
            case (byte)'<':
                if (_pos + 1 < _end && _buffer[_pos + 1] == (byte)'<')
                {
                    _pos += 2;
                    return Token.Delimiter(TokenType.DictStart);
                }
                return ReadHexString();
            case (byte)'>':
                if (_pos + 1 < _end && _buffer[_pos + 1] == (byte)'>')
                {
                    _pos += 2;
                    return Token.Delimiter(TokenType.DictEnd);
                }
                _pos++;
                return Token.Keyword(">");
            case (byte)'/':
                return ReadName();
            case (byte)'(':
                return ReadLiteralString();
            default:
                if (b == '+' || b == '-' || b == '.' || ByteClass.IsDigit(b)) return ReadNumber();
                return ReadKeyword();
        }
    }

    private void SkipWhitespaceAndComments()
    {
        while (_pos < _end)
        {
            int b = _buffer[_pos];
            if (ByteClass.IsWhitespace(b))
            {
                _pos++;
            }
            else if (b == '%')
            {
                while (_pos < _end && !ByteClass.IsEol(_buffer[_pos])) _pos++;
            }
            else
            {
                break;
            }
        }
    }

    private Token ReadNumber()
    {
        int start = _pos;
        bool isReal = false;
        if (_buffer[_pos] == '+' || _buffer[_pos] == '-') _pos++;
        while (_pos < _end)
        {
            int b = _buffer[_pos];
            if (ByteClass.IsDigit(b))
            {
                _pos++;
            }
            else if (b == '.' && !isReal)
            {
                isReal = true;
                _pos++;
            }
            else
            {
                break;
            }
        }

        string text = Ascii(start, _pos);
        if (isReal)
        {
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double real);
            return Token.Real(real);
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
        {
            return Token.Integer(value);
        }

        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double fallback);
        return Token.Real(fallback);
    }

    private Token ReadName()
    {
        _pos++; // skip '/'
        var bytes = new List<byte>();
        while (_pos < _end)
        {
            int b = _buffer[_pos];
            if (!ByteClass.IsRegular(b)) break;

            if (b == '#' && _pos + 2 < _end && IsHex(_buffer[_pos + 1]) && IsHex(_buffer[_pos + 2]))
            {
                bytes.Add((byte)((HexValue(_buffer[_pos + 1]) << 4) | HexValue(_buffer[_pos + 2])));
                _pos += 3;
            }
            else
            {
                bytes.Add((byte)b);
                _pos++;
            }
        }
        return Token.Name(Encoding.UTF8.GetString(bytes.ToArray()));
    }

    private Token ReadKeyword()
    {
        int start = _pos;
        while (_pos < _end && ByteClass.IsRegular(_buffer[_pos])) _pos++;
        if (_pos == start)
        {
            _pos++; // consume one stray delimiter byte to guarantee progress
        }
        return Token.Keyword(Ascii(start, _pos));
    }

    private Token ReadHexString()
    {
        _pos++; // skip '<'
        var bytes = new List<byte>();
        int high = -1;
        while (_pos < _end)
        {
            int b = _buffer[_pos++];
            if (b == '>') break;
            if (ByteClass.IsWhitespace(b)) continue;
            if (!IsHex(b)) continue;

            int v = HexValue(b);
            if (high < 0)
            {
                high = v;
            }
            else
            {
                bytes.Add((byte)((high << 4) | v));
                high = -1;
            }
        }
        if (high >= 0) bytes.Add((byte)(high << 4)); // odd trailing digit is padded with 0
        return Token.String(bytes.ToArray(), hex: true);
    }

    private Token ReadLiteralString()
    {
        _pos++; // skip '('
        var bytes = new List<byte>();
        int depth = 1;
        while (_pos < _end)
        {
            int b = _buffer[_pos++];
            if (b == '\\')
            {
                if (_pos >= _end) break;
                int e = _buffer[_pos++];
                switch (e)
                {
                    case (byte)'n': bytes.Add(0x0A); break;
                    case (byte)'r': bytes.Add(0x0D); break;
                    case (byte)'t': bytes.Add(0x09); break;
                    case (byte)'b': bytes.Add(0x08); break;
                    case (byte)'f': bytes.Add(0x0C); break;
                    case (byte)'(': bytes.Add((byte)'('); break;
                    case (byte)')': bytes.Add((byte)')'); break;
                    case (byte)'\\': bytes.Add((byte)'\\'); break;
                    case 0x0D:
                        if (_pos < _end && _buffer[_pos] == 0x0A) _pos++; // CRLF line continuation
                        break;
                    case 0x0A:
                        break; // LF line continuation
                    default:
                        if (e >= '0' && e <= '7')
                        {
                            int val = e - '0';
                            for (int k = 0; k < 2 && _pos < _end && _buffer[_pos] >= '0' && _buffer[_pos] <= '7'; k++)
                            {
                                val = (val << 3) | (_buffer[_pos] - '0');
                                _pos++;
                            }
                            bytes.Add((byte)(val & 0xFF));
                        }
                        else
                        {
                            bytes.Add((byte)e);
                        }
                        break;
                }
            }
            else if (b == '(')
            {
                depth++;
                bytes.Add((byte)'(');
            }
            else if (b == ')')
            {
                depth--;
                if (depth == 0) break;
                bytes.Add((byte)')');
            }
            else
            {
                bytes.Add((byte)b);
            }
        }
        return Token.String(bytes.ToArray(), hex: false);
    }

    private string Ascii(int start, int end) => Encoding.ASCII.GetString(_buffer, start, end - start);

    private static bool IsHex(int b)
        => (b >= '0' && b <= '9') || (b >= 'A' && b <= 'F') || (b >= 'a' && b <= 'f');

    private static int HexValue(int b)
        => b <= '9' ? b - '0' : (b <= 'F' ? b - 'A' + 10 : b - 'a' + 10);
}
