using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZeroDep.Color;

/// <summary>
/// Type 4 — PostScript calculator function (ISO 32000-2 §7.10.5). A small, bounded stack machine over the
/// allowed arithmetic, comparison, boolean, and stack operators plus <c>if</c>/<c>ifelse</c>. Inputs are
/// pushed onto the stack; after execution the top <see cref="OutputCount"/> values are the outputs.
/// </summary>
internal sealed class PostScriptFunction : PdfFunction
{
    private const int MaxSteps = 1_000_000; // guard against pathological/malformed programs

    private readonly PsProc _program;
    private readonly int _outputs;

    private PostScriptFunction(double[] domain, double[] range, PsProc program)
        : base(domain, range)
    {
        _program = program;
        _outputs = range.Length / 2;
    }

    public override int OutputCount => _outputs;

    public static PostScriptFunction Create(byte[] data, double[] domain, double[]? range)
    {
        var text = Encoding.ASCII.GetString(data);
        PsProc program = Parse(text);
        return new PostScriptFunction(domain, range ?? Array.Empty<double>(), program);
    }

    protected override double[] EvaluateCore(double[] input)
    {
        var stack = new List<double>(input.Length + 16);
        stack.AddRange(input);

        int steps = 0;
        Execute(_program, stack, ref steps);

        int n = _outputs;
        var outv = new double[n];
        int start = stack.Count - n;
        for (int j = 0; j < n; j++)
        {
            int idx = start + j;
            outv[j] = idx >= 0 && idx < stack.Count ? stack[idx] : 0.0;
        }

        return outv;
    }

    // ---- execution ----

    private static void Execute(PsProc proc, List<double> s, ref int steps)
    {
        var pendingProcs = new List<PsProc>(2);

        foreach (PsToken t in proc.Tokens)
        {
            if (++steps > MaxSteps)
            {
                return;
            }

            if (t.IsNumber)
            {
                s.Add(t.Number);
                continue;
            }

            if (t.Proc is { } nested)
            {
                pendingProcs.Add(nested);
                continue;
            }

            switch (t.Op)
            {
                case "if":
                {
                    PsProc body = pendingProcs.Count > 0 ? pendingProcs[pendingProcs.Count - 1] : EmptyProc;
                    pendingProcs.Clear();
                    bool cond = Pop(s) != 0.0;
                    if (cond)
                    {
                        Execute(body, s, ref steps);
                    }

                    break;
                }

                case "ifelse":
                {
                    PsProc p2 = pendingProcs.Count > 1 ? pendingProcs[pendingProcs.Count - 1] : EmptyProc;
                    PsProc p1 = pendingProcs.Count > 1 ? pendingProcs[pendingProcs.Count - 2] : EmptyProc;
                    pendingProcs.Clear();
                    bool cond = Pop(s) != 0.0;
                    Execute(cond ? p1 : p2, s, ref steps);
                    break;
                }

                default:
                    pendingProcs.Clear();
                    ApplyOperator(t.Op, s);
                    break;
            }
        }
    }

    private static void ApplyOperator(string op, List<double> s)
    {
        switch (op)
        {
            // arithmetic
            case "add": { double b = Pop(s), a = Pop(s); s.Add(a + b); break; }
            case "sub": { double b = Pop(s), a = Pop(s); s.Add(a - b); break; }
            case "mul": { double b = Pop(s), a = Pop(s); s.Add(a * b); break; }
            case "div": { double b = Pop(s), a = Pop(s); s.Add(b == 0 ? 0 : a / b); break; }
            case "idiv": { long b = (long)Pop(s), a = (long)Pop(s); s.Add(b == 0 ? 0 : a / b); break; }
            case "mod": { long b = (long)Pop(s), a = (long)Pop(s); s.Add(b == 0 ? 0 : a % b); break; }
            case "neg": s.Add(-Pop(s)); break;
            case "abs": s.Add(Math.Abs(Pop(s))); break;
            case "sqrt": s.Add(Math.Sqrt(Math.Max(0, Pop(s)))); break;
            case "sin": s.Add(Math.Sin(Pop(s) * Math.PI / 180.0)); break;
            case "cos": s.Add(Math.Cos(Pop(s) * Math.PI / 180.0)); break;
            case "atan": { double den = Pop(s), num = Pop(s); double d = Math.Atan2(num, den) * 180.0 / Math.PI; if (d < 0) d += 360.0; s.Add(d); break; }
            case "exp": { double e = Pop(s), b = Pop(s); s.Add(Math.Pow(b, e)); break; }
            case "ln": s.Add(Math.Log(Math.Max(double.Epsilon, Pop(s)))); break;
            case "log": s.Add(Math.Log10(Math.Max(double.Epsilon, Pop(s)))); break;
            case "cvi": s.Add((long)Pop(s)); break;
            case "cvr": break; // already real
            case "floor": s.Add(Math.Floor(Pop(s))); break;
            case "ceiling": s.Add(Math.Ceiling(Pop(s))); break;
            case "round": s.Add(Math.Round(Pop(s), MidpointRounding.AwayFromZero)); break;
            case "truncate": s.Add(Math.Truncate(Pop(s))); break;

            // comparison -> boolean (1/0)
            case "eq": { double b = Pop(s), a = Pop(s); s.Add(a == b ? 1 : 0); break; }
            case "ne": { double b = Pop(s), a = Pop(s); s.Add(a != b ? 1 : 0); break; }
            case "gt": { double b = Pop(s), a = Pop(s); s.Add(a > b ? 1 : 0); break; }
            case "ge": { double b = Pop(s), a = Pop(s); s.Add(a >= b ? 1 : 0); break; }
            case "lt": { double b = Pop(s), a = Pop(s); s.Add(a < b ? 1 : 0); break; }
            case "le": { double b = Pop(s), a = Pop(s); s.Add(a <= b ? 1 : 0); break; }

            // boolean / bitwise
            case "and": { long b = (long)Pop(s), a = (long)Pop(s); s.Add(a & b); break; }
            case "or": { long b = (long)Pop(s), a = (long)Pop(s); s.Add(a | b); break; }
            case "xor": { long b = (long)Pop(s), a = (long)Pop(s); s.Add(a ^ b); break; }
            case "not": { double a = Pop(s); s.Add(a == 0 ? 1 : 0); break; }
            case "bitshift": { int sh = (int)Pop(s); long a = (long)Pop(s); s.Add(sh >= 0 ? a << sh : a >> -sh); break; }
            case "true": s.Add(1); break;
            case "false": s.Add(0); break;

            // stack operators
            case "pop": Pop(s); break;
            case "exch": { double b = Pop(s), a = Pop(s); s.Add(b); s.Add(a); break; }
            case "dup": { double a = Peek(s); s.Add(a); break; }
            case "copy":
            {
                int nn = (int)Pop(s);
                int count = s.Count;
                for (int i = 0; i < nn; i++)
                {
                    int idx = count - nn + i;
                    s.Add(idx >= 0 && idx < count ? s[idx] : 0.0);
                }

                break;
            }

            case "index":
            {
                int nn = (int)Pop(s);
                int idx = s.Count - 1 - nn;
                s.Add(idx >= 0 && idx < s.Count ? s[idx] : 0.0);
                break;
            }

            case "roll":
            {
                int j = (int)Pop(s);
                int nn = (int)Pop(s);
                Roll(s, nn, j);
                break;
            }

            default:
                break; // unknown token: ignore (keep deterministic, never throw mid-eval)
        }
    }

    private static void Roll(List<double> s, int n, int j)
    {
        if (n <= 0 || n > s.Count)
        {
            return;
        }

        int start = s.Count - n;
        var slice = new double[n];
        for (int i = 0; i < n; i++)
        {
            slice[i] = s[start + i];
        }

        j = ((j % n) + n) % n; // normalize, positive = roll toward top
        var rolled = new double[n];
        for (int i = 0; i < n; i++)
        {
            rolled[(i + j) % n] = slice[i];
        }

        for (int i = 0; i < n; i++)
        {
            s[start + i] = rolled[i];
        }
    }

    private static double Pop(List<double> s)
    {
        if (s.Count == 0)
        {
            return 0.0;
        }

        double v = s[s.Count - 1];
        s.RemoveAt(s.Count - 1);
        return v;
    }

    private static double Peek(List<double> s) => s.Count == 0 ? 0.0 : s[s.Count - 1];

    // ---- parsing ----

    private static readonly PsProc EmptyProc = new PsProc(new List<PsToken>());

    private static PsProc Parse(string text)
    {
        List<string> raw = Tokenize(text);
        int pos = 0;

        // Skip to the outer '{'.
        while (pos < raw.Count && raw[pos] != "{")
        {
            pos++;
        }

        if (pos >= raw.Count)
        {
            return EmptyProc;
        }

        pos++; // consume outer '{'
        return ParseProc(raw, ref pos);
    }

    private static PsProc ParseProc(List<string> raw, ref int pos)
    {
        var tokens = new List<PsToken>();
        while (pos < raw.Count)
        {
            string tok = raw[pos++];
            if (tok == "}")
            {
                break;
            }

            if (tok == "{")
            {
                PsProc nested = ParseProc(raw, ref pos);
                tokens.Add(PsToken.FromProc(nested));
                continue;
            }

            if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out double num))
            {
                tokens.Add(PsToken.FromNumber(num));
            }
            else
            {
                tokens.Add(PsToken.FromOp(tok));
            }
        }

        return new PsProc(tokens);
    }

    private static List<string> Tokenize(string text)
    {
        var result = new List<string>();
        var sb = new StringBuilder();

        void Flush()
        {
            if (sb.Length > 0)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
        }

        foreach (char c in text)
        {
            if (c == '{' || c == '}')
            {
                Flush();
                result.Add(c.ToString());
            }
            else if (char.IsWhiteSpace(c))
            {
                Flush();
            }
            else if (c == '%')
            {
                // PostScript comment to end of line — treat '%' as a separator (rare in functions).
                Flush();
            }
            else
            {
                sb.Append(c);
            }
        }

        Flush();
        return result;
    }

    private sealed class PsProc
    {
        public PsProc(List<PsToken> tokens) => Tokens = tokens;

        public List<PsToken> Tokens { get; }
    }

    private readonly struct PsToken
    {
        private PsToken(bool isNumber, double number, string op, PsProc? proc)
        {
            IsNumber = isNumber;
            Number = number;
            Op = op;
            Proc = proc;
        }

        public bool IsNumber { get; }

        public double Number { get; }

        public string Op { get; }

        public PsProc? Proc { get; }

        public static PsToken FromNumber(double n) => new PsToken(true, n, string.Empty, null);

        public static PsToken FromOp(string op) => new PsToken(false, 0, op, null);

        public static PsToken FromProc(PsProc p) => new PsToken(false, 0, string.Empty, p);
    }
}
