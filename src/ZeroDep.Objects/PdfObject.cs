using System;
using System.Collections.Generic;
using System.Text;

namespace ZeroDep.Objects;

/// <summary>Base type for every parsed PDF object (ISO 32000-2 §7.3).</summary>
public abstract class PdfObject
{
}

/// <summary>The PDF <c>null</c> object.</summary>
public sealed class PdfNull : PdfObject
{
    private PdfNull()
    {
    }

    /// <summary>The shared singleton instance.</summary>
    public static PdfNull Instance { get; } = new PdfNull();
}

/// <summary>A PDF boolean (<c>true</c>/<c>false</c>).</summary>
public sealed class PdfBoolean : PdfObject
{
    private PdfBoolean(bool value) => Value = value;

    /// <summary>The boolean value.</summary>
    public bool Value { get; }

    /// <summary>The shared <c>true</c> instance.</summary>
    public static PdfBoolean True { get; } = new PdfBoolean(true);

    /// <summary>The shared <c>false</c> instance.</summary>
    public static PdfBoolean False { get; } = new PdfBoolean(false);
}

/// <summary>Base type for PDF numeric objects.</summary>
public abstract class PdfNumber : PdfObject
{
    /// <summary>The value as a double.</summary>
    public abstract double AsDouble { get; }

    /// <summary>The value as a 64-bit integer (truncated for reals).</summary>
    public abstract long AsInt64 { get; }
}

/// <summary>A PDF integer object.</summary>
public sealed class PdfInteger : PdfNumber
{
    /// <summary>Creates an integer object.</summary>
    /// <param name="value">The integer value.</param>
    public PdfInteger(long value) => Value = value;

    /// <summary>The integer value.</summary>
    public long Value { get; }

    /// <inheritdoc/>
    public override double AsDouble => Value;

    /// <inheritdoc/>
    public override long AsInt64 => Value;
}

/// <summary>A PDF real (floating-point) object.</summary>
public sealed class PdfReal : PdfNumber
{
    /// <summary>Creates a real object.</summary>
    /// <param name="value">The real value.</param>
    public PdfReal(double value) => Value = value;

    /// <summary>The real value.</summary>
    public double Value { get; }

    /// <inheritdoc/>
    public override double AsDouble => Value;

    /// <inheritdoc/>
    public override long AsInt64 => (long)Value;
}

/// <summary>A PDF name object (e.g. <c>/Type</c>), stored without the leading slash.</summary>
public sealed class PdfName : PdfObject
{
    /// <summary>Creates a name object.</summary>
    /// <param name="value">The decoded name (no leading slash).</param>
    public PdfName(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>The decoded name text.</summary>
    public string Value { get; }
}

/// <summary>A PDF string object (literal or hexadecimal), holding raw bytes.</summary>
public sealed class PdfString : PdfObject
{
    private readonly byte[] _bytes;

    /// <summary>Creates a string object from raw bytes.</summary>
    /// <param name="bytes">The decoded raw bytes.</param>
    /// <param name="isHexString">Whether the source syntax was a hex string.</param>
    public PdfString(byte[] bytes, bool isHexString)
    {
        _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        IsHexString = isHexString;
    }

    /// <summary>Whether the source syntax was a hexadecimal string.</summary>
    public bool IsHexString { get; }

    /// <summary>The number of raw bytes.</summary>
    public int Length => _bytes.Length;

    /// <summary>Returns a copy of the raw bytes.</summary>
    public byte[] ToArray()
    {
        var copy = new byte[_bytes.Length];
        Array.Copy(_bytes, copy, _bytes.Length);
        return copy;
    }

    /// <summary>
    /// Decodes the string to text: UTF-16BE when a byte-order mark is present,
    /// otherwise PDFDocEncoding approximated as Latin-1.
    /// </summary>
    public string GetText()
    {
        if (_bytes.Length >= 2 && _bytes[0] == 0xFE && _bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(_bytes, 2, _bytes.Length - 2);
        }

        var chars = new char[_bytes.Length];
        for (int i = 0; i < _bytes.Length; i++) chars[i] = (char)_bytes[i];
        return new string(chars);
    }
}

/// <summary>A PDF array object.</summary>
public sealed class PdfArray : PdfObject
{
    private readonly IReadOnlyList<PdfObject> _items;

    /// <summary>Creates an array object.</summary>
    /// <param name="items">The ordered elements.</param>
    public PdfArray(IReadOnlyList<PdfObject> items)
        => _items = items ?? throw new ArgumentNullException(nameof(items));

    /// <summary>The number of elements.</summary>
    public int Count => _items.Count;

    /// <summary>The element at <paramref name="index"/>.</summary>
    public PdfObject this[int index] => _items[index];

    /// <summary>The elements.</summary>
    public IReadOnlyList<PdfObject> Items => _items;
}

/// <summary>A PDF dictionary object, keyed by name (without the leading slash).</summary>
public sealed class PdfDictionary : PdfObject
{
    private readonly Dictionary<string, PdfObject> _entries;

    /// <summary>Creates a dictionary from the given entries (copied).</summary>
    /// <param name="entries">The name/value pairs.</param>
    public PdfDictionary(IDictionary<string, PdfObject> entries)
    {
        if (entries is null) throw new ArgumentNullException(nameof(entries));
        _entries = new Dictionary<string, PdfObject>(entries, StringComparer.Ordinal);
    }

    /// <summary>The number of entries.</summary>
    public int Count => _entries.Count;

    /// <summary>The entry names.</summary>
    public IEnumerable<string> Keys => _entries.Keys;

    /// <summary>Whether the dictionary contains <paramref name="name"/>.</summary>
    public bool ContainsKey(string name) => _entries.ContainsKey(name);

    /// <summary>Gets the value for <paramref name="name"/>, or null if absent.</summary>
    public PdfObject? this[string name] => _entries.TryGetValue(name, out var value) ? value : null;

    /// <summary>Tries to get the value for <paramref name="name"/>.</summary>
    public bool TryGetValue(string name, out PdfObject? value) => _entries.TryGetValue(name, out value);
}

/// <summary>An indirect reference such as <c>12 0 R</c>.</summary>
public sealed class PdfReference : PdfObject, IEquatable<PdfReference>
{
    /// <summary>Creates a reference.</summary>
    /// <param name="objectNumber">The referenced object number.</param>
    /// <param name="generation">The referenced generation number.</param>
    public PdfReference(int objectNumber, int generation)
    {
        ObjectNumber = objectNumber;
        Generation = generation;
    }

    /// <summary>The referenced object number.</summary>
    public int ObjectNumber { get; }

    /// <summary>The referenced generation number.</summary>
    public int Generation { get; }

    /// <inheritdoc/>
    public bool Equals(PdfReference? other)
        => other is not null && other.ObjectNumber == ObjectNumber && other.Generation == Generation;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as PdfReference);

    /// <inheritdoc/>
    public override int GetHashCode() => (ObjectNumber * 397) ^ Generation;
}

/// <summary>A PDF stream object: a dictionary plus its raw (still-encoded) data.</summary>
public sealed class PdfStream : PdfObject
{
    private readonly byte[] _rawData;

    /// <summary>Creates a stream object.</summary>
    /// <param name="dictionary">The stream dictionary.</param>
    /// <param name="rawData">The raw, still-encoded stream bytes.</param>
    public PdfStream(PdfDictionary dictionary, byte[] rawData)
    {
        Dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        _rawData = rawData ?? throw new ArgumentNullException(nameof(rawData));
    }

    /// <summary>The stream dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>The length of the raw (encoded) data.</summary>
    public int RawLength => _rawData.Length;

    /// <summary>Returns a copy of the raw (encoded) stream bytes.</summary>
    public byte[] GetRawBytes()
    {
        var copy = new byte[_rawData.Length];
        Array.Copy(_rawData, copy, _rawData.Length);
        return copy;
    }
}
