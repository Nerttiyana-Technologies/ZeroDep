using System;
using System.Collections.Generic;
using System.Text;
using ZeroDep.Lexing;
using ZeroDep.Objects;

namespace ZeroDep.Content;

/// <summary>
/// Walks a decoded content stream as an operator state machine, tracking the graphics-state stack,
/// CTM, and text state, and reports image placements and positioned text runs (with advance widths).
/// Tr = 3 text is the invisible OCR layer. Decoupled from the document via resolve/decode delegates.
/// </summary>
internal sealed class ContentInterpreter
{
    private const int MaxFormDepth = 12;
    private const double TjSpaceThreshold = 180.0;

    // Ruling-line detection: a painted path segment counts as a "ruling line" when it is axis-aligned
    // (near-horizontal or near-vertical) and at least this long (device points). Tuned for the table hint.
    // Filled paths only count when they are a *thin* rectangle (a rule), not a background/shading fill.
    private const double MinRuleLength = 24.0;
    private const double RuleAxisEps = 0.75;
    private const double ThinRectEps = 3.0;

    private readonly Func<PdfObject, PdfObject> _resolve;
    private readonly Func<PdfStream, byte[]> _decode;

    public ContentInterpreter(Func<PdfObject, PdfObject> resolve, Func<PdfStream, byte[]> decode)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _decode = decode ?? throw new ArgumentNullException(nameof(decode));
    }

    /// <summary>Runs the interpreter and returns image placements only (back-compat for DPI).</summary>
    public List<ImagePlacement> Run(byte[] content, PdfDictionary resources, Matrix initial)
        => RunAll(content, resources, initial).Images;

    /// <summary>Runs the interpreter and returns both images and text runs.</summary>
    public ContentResult RunAll(byte[] content, PdfDictionary resources, Matrix initial)
    {
        var result = new ContentResult();
        Execute(content, resources, initial, result, 0);
        return result;
    }

    private void Execute(byte[] content, PdfDictionary resources, Matrix baseCtm, ContentResult result, int depth)
    {
        if (depth > MaxFormDepth) return;

        var lexer = new PdfLexer(content, 0, content.Length);
        var operands = new List<PdfObject>();
        var graphicsStack = new Stack<GraphicsSnapshot>();
        var fontCache = new Dictionary<string, FontInfo?>(StringComparer.Ordinal);

        Matrix ctm = baseCtm;
        FontInfo? font = null;
        double fontSize = 0, leading = 0, charSpacing = 0, wordSpacing = 0, horizScale = 100;
        int renderMode = 0;
        Matrix textMatrix = Matrix.Identity, lineMatrix = Matrix.Identity;

        double GlyphAdvance(Glyph g)
            => (((g.WidthEm / 1000.0) * fontSize) + charSpacing + (g.IsSpace ? wordSpacing : 0)) * (horizScale / 100.0);

        void Emit(string text, double advance, int auth, int fallback, int unmapped)
        {
            Matrix start = Matrix.Multiply(textMatrix, ctm);
            Matrix advancedText = Matrix.Multiply(Translate(advance, 0), textMatrix);
            if (text.Length > 0)
            {
                Matrix end = Matrix.Multiply(advancedText, ctm);
                double width = Hypot(end.E - start.E, end.F - start.F);
                double size = fontSize * Hypot(start.C, start.D);
                result.TextRuns.Add(new TextRun(text, start.E, start.F, width, size, renderMode, auth, fallback, unmapped, font?.SpaceWidthEm ?? 0.25));
            }
            textMatrix = advancedText;
        }

        void Tally(Glyph g, ref int auth, ref int fallback, ref int unmapped)
        {
            switch (g.Tier)
            {
                case DecodeTier.Authoritative: auth++; break;
                case DecodeTier.Fallback: fallback++; break;
                default: unmapped++; break;
            }
        }

        void ShowSimple(PdfString s)
        {
            if (font is null) return;
            var sb = new StringBuilder();
            double advance = 0;
            int auth = 0, fallback = 0, unmapped = 0;
            foreach (Glyph g in font.Decode(s.ToArray()))
            {
                sb.Append(g.Text);
                advance += GlyphAdvance(g);
                Tally(g, ref auth, ref fallback, ref unmapped);
            }
            Emit(sb.ToString(), advance, auth, fallback, unmapped);
        }

        void NextLine()
        {
            lineMatrix = Matrix.Multiply(Translate(0, -leading), lineMatrix);
            textMatrix = lineMatrix;
        }

        var pathSegments = new List<(double X0, double Y0, double X1, double Y1, bool ThinRule)>();
        double curX = 0, curY = 0, subStartX = 0, subStartY = 0;

        (double X, double Y) Device(double x, double y)
            => (((x * ctm.A) + (y * ctm.C)) + ctm.E, ((x * ctm.B) + (y * ctm.D)) + ctm.F);

        void MoveTo(double x, double y)
        {
            (curX, curY) = Device(x, y);
            subStartX = curX;
            subStartY = curY;
        }

        void LineTo(double x, double y)
        {
            (double dx, double dy) = Device(x, y);
            pathSegments.Add((curX, curY, dx, dy, false));
            curX = dx;
            curY = dy;
        }

        void RectPath(double x, double y, double w, double h)
        {
            (double x0, double y0) = Device(x, y);
            (double x1, double y1) = Device(x + w, y);
            (double x2, double y2) = Device(x + w, y + h);
            (double x3, double y3) = Device(x, y + h);
            bool thin = Math.Min(Hypot(x1 - x0, y1 - y0), Hypot(x3 - x0, y3 - y0)) <= ThinRectEps;
            pathSegments.Add((x0, y0, x1, y1, thin));
            pathSegments.Add((x1, y1, x2, y2, thin));
            pathSegments.Add((x2, y2, x3, y3, thin));
            pathSegments.Add((x3, y3, x0, y0, thin));
            curX = x0;
            curY = y0;
            subStartX = x0;
            subStartY = y0;
        }

        // stroke=true counts every axis-aligned long segment; fills count only thin rectangles (rules).
        void PaintPath(bool stroke)
        {
            foreach ((double X0, double Y0, double X1, double Y1, bool ThinRule) seg in pathSegments)
            {
                double dx = seg.X1 - seg.X0;
                double dy = seg.Y1 - seg.Y0;
                double len = Math.Max(Math.Abs(dx), Math.Abs(dy));
                bool axisAligned = Math.Abs(dx) < RuleAxisEps || Math.Abs(dy) < RuleAxisEps;
                if (axisAligned && len >= MinRuleLength && (stroke || seg.ThinRule))
                {
                    result.RulingLineCount++;
                }
            }

            pathSegments.Clear();
        }

        while (true)
        {
            Token token = lexer.Next();
            if (token.Type == TokenType.Eof) break;

            if (token.Type != TokenType.Keyword)
            {
                if (token.Type != TokenType.ArrayEnd && token.Type != TokenType.DictEnd)
                {
                    operands.Add(ParseOperand(lexer, token));
                }
                continue;
            }

            string op = token.Text!;
            switch (op)
            {
                case "true": operands.Add(PdfBoolean.True); continue;
                case "false": operands.Add(PdfBoolean.False); continue;
                case "null": operands.Add(PdfNull.Instance); continue;

                case "q": graphicsStack.Push(new GraphicsSnapshot(ctm, font, fontSize, renderMode, leading, charSpacing, wordSpacing, horizScale)); break;
                case "Q":
                    if (graphicsStack.Count > 0)
                    {
                        GraphicsSnapshot s = graphicsStack.Pop();
                        ctm = s.Ctm; font = s.Font; fontSize = s.FontSize; renderMode = s.RenderMode;
                        leading = s.Leading; charSpacing = s.CharSpacing; wordSpacing = s.WordSpacing; horizScale = s.HorizScale;
                    }
                    break;
                case "cm":
                    if (operands.Count >= 6) ctm = Matrix.Multiply(MatrixFromOperands(operands), ctm);
                    break;

                case "Do":
                    if (operands.Count >= 1 && operands[operands.Count - 1] is PdfName name) HandleDo(name.Value, resources, ctm, result, depth);
                    break;
                case "BI": ParseInlineImage(lexer, ctm, result); break;

                case "BT": textMatrix = Matrix.Identity; lineMatrix = Matrix.Identity; break;
                case "ET": break;
                case "Td":
                    if (operands.Count >= 2)
                    {
                        lineMatrix = Matrix.Multiply(Translate(NumAt(operands, 2), NumAt(operands, 1)), lineMatrix);
                        textMatrix = lineMatrix;
                    }
                    break;
                case "TD":
                    if (operands.Count >= 2)
                    {
                        leading = -NumAt(operands, 1);
                        lineMatrix = Matrix.Multiply(Translate(NumAt(operands, 2), NumAt(operands, 1)), lineMatrix);
                        textMatrix = lineMatrix;
                    }
                    break;
                case "Tm":
                    if (operands.Count >= 6) { textMatrix = MatrixFromOperands(operands); lineMatrix = textMatrix; }
                    break;
                case "T*": NextLine(); break;
                case "TL": if (operands.Count >= 1) leading = NumAt(operands, 1); break;
                case "Tc": if (operands.Count >= 1) charSpacing = NumAt(operands, 1); break;
                case "Tw": if (operands.Count >= 1) wordSpacing = NumAt(operands, 1); break;
                case "Tz": if (operands.Count >= 1) horizScale = NumAt(operands, 1); break;
                case "Tf":
                    if (operands.Count >= 2)
                    {
                        fontSize = NumAt(operands, 1);
                        if (operands[operands.Count - 2] is PdfName fontName)
                        {
                            font = ResolveFont(fontName.Value, resources, fontCache);
                            result.FontNames.Add(fontName.Value);
                        }
                    }
                    break;
                case "Tr": if (operands.Count >= 1) renderMode = (int)NumAt(operands, 1); break;

                case "Tj":
                    if (operands.Count >= 1 && operands[operands.Count - 1] is PdfString tj) ShowSimple(tj);
                    break;
                case "TJ":
                    if (operands.Count >= 1 && operands[operands.Count - 1] is PdfArray tjArray && font is not null)
                    {
                        var sb = new StringBuilder();
                        double advance = 0;
                        int auth = 0, fallback = 0, unmapped = 0;
                        foreach (PdfObject item in tjArray.Items)
                        {
                            if (item is PdfString s)
                            {
                                foreach (Glyph g in font.Decode(s.ToArray())) { sb.Append(g.Text); advance += GlyphAdvance(g); Tally(g, ref auth, ref fallback, ref unmapped); }
                            }
                            else if (item is PdfNumber num)
                            {
                                advance += -num.AsDouble / 1000.0 * fontSize * (horizScale / 100.0);
                                if (num.AsDouble <= -TjSpaceThreshold) sb.Append(' ');
                            }
                        }
                        Emit(sb.ToString(), advance, auth, fallback, unmapped);
                    }
                    break;
                case "'":
                    NextLine();
                    if (operands.Count >= 1 && operands[operands.Count - 1] is PdfString quote) ShowSimple(quote);
                    break;
                case "\"":
                    NextLine();
                    if (operands.Count >= 1 && operands[operands.Count - 1] is PdfString dquote) ShowSimple(dquote);
                    break;

                // Path construction (coordinates → device space for ruling-line detection).
                case "m": if (operands.Count >= 2) MoveTo(NumAt(operands, 2), NumAt(operands, 1)); break;
                case "l": if (operands.Count >= 2) LineTo(NumAt(operands, 2), NumAt(operands, 1)); break;
                case "re": if (operands.Count >= 4) RectPath(NumAt(operands, 4), NumAt(operands, 3), NumAt(operands, 2), NumAt(operands, 1)); break;
                case "c":
                case "v":
                case "y":
                    // Bézier curves are not rulings — advance the current point to the endpoint only.
                    if (operands.Count >= 2) (curX, curY) = Device(NumAt(operands, 2), NumAt(operands, 1));
                    break;
                case "h":
                    pathSegments.Add((curX, curY, subStartX, subStartY, false));
                    curX = subStartX;
                    curY = subStartY;
                    break;

                // Path painting — count ruling lines, then clear the path.
                case "S":
                case "s":
                case "B":
                case "B*":
                case "b":
                case "b*":
                    PaintPath(stroke: true);
                    break;
                case "f":
                case "F":
                case "f*":
                    PaintPath(stroke: false);
                    break;
                case "n":
                    pathSegments.Clear();
                    break;

                default: break;
            }
            operands.Clear();
        }
    }

    private void HandleDo(string name, PdfDictionary resources, Matrix ctm, ContentResult result, int depth)
    {
        if (_resolve(resources["XObject"] ?? PdfNull.Instance) is not PdfDictionary xobjects) return;
        PdfObject? entry = xobjects[name];
        if (entry is null) return;
        if (_resolve(entry) is not PdfStream stream) return;

        string? subtype = (stream.Dictionary["Subtype"] as PdfName)?.Value;
        if (subtype == "Image")
        {
            result.Images.Add(new ImagePlacement(name, stream, ctm));
        }
        else if (subtype == "Form")
        {
            Matrix formMatrix = Matrix.Identity;
            if (_resolve(stream.Dictionary["Matrix"] ?? PdfNull.Instance) is PdfArray m && m.Count >= 6) formMatrix = MatrixFromArray(m);
            PdfDictionary formResources = _resolve(stream.Dictionary["Resources"] ?? PdfNull.Instance) as PdfDictionary ?? resources;
            byte[] formContent = _decode(stream);
            Execute(formContent, formResources, Matrix.Multiply(formMatrix, ctm), result, depth + 1);
        }
    }

    private FontInfo? ResolveFont(string name, PdfDictionary resources, Dictionary<string, FontInfo?> cache)
    {
        if (cache.TryGetValue(name, out FontInfo? cached)) return cached;

        FontInfo? font = null;
        if (_resolve(resources["Font"] ?? PdfNull.Instance) is PdfDictionary fonts
            && _resolve(fonts[name] ?? PdfNull.Instance) is PdfDictionary fontDict)
        {
            font = new FontInfo(fontDict, _resolve, _decode);
        }
        cache[name] = font;
        return font;
    }

    private static Matrix Translate(double tx, double ty) => new Matrix(1, 0, 0, 1, tx, ty);

    private static double Hypot(double a, double b) => Math.Sqrt((a * a) + (b * b));

    private static double NumAt(List<PdfObject> operands, int fromEnd)
        => operands.Count >= fromEnd && operands[operands.Count - fromEnd] is PdfNumber n ? n.AsDouble : 0;

    private static Matrix MatrixFromOperands(List<PdfObject> operands)
    {
        int n = operands.Count;
        double V(int i) => operands[n - 6 + i] is PdfNumber number ? number.AsDouble : 0;
        return new Matrix(V(0), V(1), V(2), V(3), V(4), V(5));
    }

    private static Matrix MatrixFromArray(PdfArray array)
    {
        double V(int i) => array[i] is PdfNumber number ? number.AsDouble : 0;
        return new Matrix(V(0), V(1), V(2), V(3), V(4), V(5));
    }

    private static PdfObject ParseOperand(PdfLexer lexer, Token token)
    {
        switch (token.Type)
        {
            case TokenType.Integer: return new PdfInteger(token.IntValue);
            case TokenType.Real: return new PdfReal(token.RealValue);
            case TokenType.Name: return new PdfName(token.Text!);
            case TokenType.LiteralString: return new PdfString(token.Bytes!, false);
            case TokenType.HexString: return new PdfString(token.Bytes!, true);
            case TokenType.ArrayStart:
            {
                var items = new List<PdfObject>();
                while (true)
                {
                    Token x = lexer.Next();
                    if (x.Type == TokenType.ArrayEnd || x.Type == TokenType.Eof) break;
                    items.Add(ParseOperand(lexer, x));
                }
                return new PdfArray(items);
            }
            case TokenType.DictStart:
            {
                var map = new Dictionary<string, PdfObject>(StringComparer.Ordinal);
                while (true)
                {
                    Token key = lexer.Next();
                    if (key.Type == TokenType.DictEnd || key.Type == TokenType.Eof) break;
                    if (key.Type != TokenType.Name) break;
                    Token value = lexer.Next();
                    if (value.Type == TokenType.DictEnd || value.Type == TokenType.Eof) break;
                    map[key.Text!] = ParseOperand(lexer, value);
                }
                return new PdfDictionary(map);
            }
            case TokenType.Keyword:
                return token.Text switch
                {
                    "true" => PdfBoolean.True,
                    "false" => PdfBoolean.False,
                    _ => PdfNull.Instance,
                };
            default:
                return PdfNull.Instance;
        }
    }

    private static void ParseInlineImage(PdfLexer lexer, Matrix ctm, ContentResult result)
    {
        // Parse the inline-image dictionary (abbreviated keys) up to the 'ID' operator.
        var entries = new Dictionary<string, PdfObject>(StringComparer.Ordinal);
        while (true)
        {
            Token key = lexer.Next();
            if (key.Type == TokenType.Eof) return;
            if (key.Type == TokenType.Keyword && key.Text == "ID") break;
            if (key.Type != TokenType.Name) continue; // tolerate stray tokens

            Token valueToken = lexer.Next();
            if (valueToken.Type == TokenType.Eof) return;
            if (valueToken.Type == TokenType.Keyword && valueToken.Text == "ID") break;
            entries[MapInlineKey(key.Text!)] = ParseOperand(lexer, valueToken);
        }

        SkipToEndImage(lexer);

        entries["Subtype"] = new PdfName("Image");
        var image = new PdfStream(new PdfDictionary(entries), Array.Empty<byte>());
        result.Images.Add(new ImagePlacement("inline", image, ctm));
    }

    private static void SkipToEndImage(PdfLexer lexer)
    {
        byte[] buffer = lexer.Buffer;
        int end = lexer.End;
        int pos = lexer.Position;
        if (pos < end && IsWhitespace(buffer[pos])) pos++; // single whitespace after ID

        int i = pos;
        while (i + 1 < end)
        {
            if (buffer[i] == (byte)'E' && buffer[i + 1] == (byte)'I'
                && (i == 0 || IsWhitespace(buffer[i - 1]))
                && (i + 2 >= end || IsWhitespace(buffer[i + 2])))
            {
                i += 2;
                break;
            }
            i++;
        }
        lexer.Seek(i);
    }

    private static string MapInlineKey(string key)
    {
        switch (key)
        {
            case "W": return "Width";
            case "H": return "Height";
            case "BPC": return "BitsPerComponent";
            case "CS": return "ColorSpace";
            case "F": return "Filter";
            case "DP": return "DecodeParms";
            case "IM": return "ImageMask";
            case "D": return "Decode";
            case "I": return "Interpolate";
            default: return key;
        }
    }

    private static bool IsWhitespace(int b)
        => b == 0x00 || b == 0x09 || b == 0x0A || b == 0x0C || b == 0x0D || b == 0x20;

    private readonly struct GraphicsSnapshot
    {
        public GraphicsSnapshot(Matrix ctm, FontInfo? font, double fontSize, int renderMode, double leading, double charSpacing, double wordSpacing, double horizScale)
        {
            Ctm = ctm; Font = font; FontSize = fontSize; RenderMode = renderMode;
            Leading = leading; CharSpacing = charSpacing; WordSpacing = wordSpacing; HorizScale = horizScale;
        }

        public Matrix Ctm { get; }
        public FontInfo? Font { get; }
        public double FontSize { get; }
        public int RenderMode { get; }
        public double Leading { get; }
        public double CharSpacing { get; }
        public double WordSpacing { get; }
        public double HorizScale { get; }
    }
}
