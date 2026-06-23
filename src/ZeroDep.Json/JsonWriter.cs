using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZeroDep.Json;

/// <summary>
/// A minimal, dependency-free JSON writer (no System.Text.Json) — used to keep ZeroDep 100% BCL
/// on every target framework, including netstandard2.0.
/// </summary>
internal sealed class JsonWriter
{
    private readonly StringBuilder _sb = new StringBuilder();
    private readonly Stack<Frame> _stack = new Stack<Frame>();
    private readonly bool _indent;
    private bool _afterName;

    public JsonWriter(bool indent) => _indent = indent;

    public override string ToString() => _sb.ToString();

    public JsonWriter BeginObject()
    {
        BeforeValue();
        _sb.Append('{');
        _stack.Push(new Frame());
        return this;
    }

    public JsonWriter EndObject()
    {
        Frame frame = _stack.Pop();
        if (frame.HasItems) Indent();
        _sb.Append('}');
        return this;
    }

    public JsonWriter BeginArray()
    {
        BeforeValue();
        _sb.Append('[');
        _stack.Push(new Frame());
        return this;
    }

    public JsonWriter EndArray()
    {
        Frame frame = _stack.Pop();
        if (frame.HasItems) Indent();
        _sb.Append(']');
        return this;
    }

    public JsonWriter Property(string name)
    {
        Frame frame = _stack.Peek();
        if (frame.HasItems) _sb.Append(',');
        frame.HasItems = true;
        Indent();
        AppendString(name);
        _sb.Append(':');
        if (_indent) _sb.Append(' ');
        _afterName = true;
        return this;
    }

    public JsonWriter Value(string? value)
    {
        BeforeValue();
        if (value is null) _sb.Append("null");
        else AppendString(value);
        return this;
    }

    public JsonWriter Value(int value)
    {
        BeforeValue();
        _sb.Append(value.ToString(CultureInfo.InvariantCulture));
        return this;
    }

    public JsonWriter Value(double value)
    {
        BeforeValue();
        if (double.IsNaN(value) || double.IsInfinity(value)) _sb.Append('0');
        else _sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        return this;
    }

    public JsonWriter Value(bool value)
    {
        BeforeValue();
        _sb.Append(value ? "true" : "false");
        return this;
    }

    public JsonWriter Null()
    {
        BeforeValue();
        _sb.Append("null");
        return this;
    }

    private void BeforeValue()
    {
        if (_afterName)
        {
            _afterName = false;
            return;
        }
        if (_stack.Count > 0)
        {
            Frame frame = _stack.Peek();
            if (frame.HasItems) _sb.Append(',');
            frame.HasItems = true;
            Indent();
        }
    }

    private void Indent()
    {
        if (!_indent) return;
        _sb.Append('\n');
        _sb.Append(' ', _stack.Count * 2);
    }

    private void AppendString(string s)
    {
        _sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': _sb.Append("\\\""); break;
                case '\\': _sb.Append("\\\\"); break;
                case '\b': _sb.Append("\\b"); break;
                case '\f': _sb.Append("\\f"); break;
                case '\n': _sb.Append("\\n"); break;
                case '\r': _sb.Append("\\r"); break;
                case '\t': _sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        _sb.Append("\\u");
                        _sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        _sb.Append(c);
                    }
                    break;
            }
        }
        _sb.Append('"');
    }

    private sealed class Frame
    {
        public bool HasItems;
    }
}
