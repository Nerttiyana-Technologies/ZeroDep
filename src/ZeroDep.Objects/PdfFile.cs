using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ZeroDep.Abstractions;
using ZeroDep.IO;
using ZeroDep.Lexing;
using ZeroDep.Security;

namespace ZeroDep.Objects;

/// <summary>
/// A low-level opened PDF file: resolves the cross-reference (classic tables, xref streams,
/// object streams, and <c>/Prev</c>/hybrid chains) and materializes indirect objects on demand.
/// </summary>
internal sealed class PdfFile : IDisposable
{
    private static readonly byte[] StartXrefKeyword = Encoding.ASCII.GetBytes("startxref");
    private static readonly byte[] TrailerKeyword = Encoding.ASCII.GetBytes("trailer");
    private static readonly byte[] XrefKeyword = Encoding.ASCII.GetBytes("xref");
    private static readonly byte[] EndObjKeyword = Encoding.ASCII.GetBytes("endobj");

    private readonly PdfByteSource _source;
    private readonly Dictionary<int, PdfObject> _objectCache = new Dictionary<int, PdfObject>();
    private readonly Dictionary<int, ObjStmData?> _objectStreamCache = new Dictionary<int, ObjStmData?>();

    private Dictionary<int, XrefEntry>? _xref;
    private PdfDictionary? _trailer;
    private StandardSecurityHandler? _handler;
    private int _encryptObjectNumber = -1;

    private PdfFile(PdfByteSource source) => _source = source;

    /// <summary>The document trailer (newest), providing <c>/Root</c>, <c>/Size</c>, etc.</summary>
    public PdfDictionary Trailer => _trailer!;

    /// <summary>The set of object numbers known to the cross-reference.</summary>
    public IReadOnlyCollection<int> KnownObjectNumbers => _xref!.Keys;

    /// <summary>Whether the document declares encryption.</summary>
    public bool IsEncrypted { get; private set; }

    /// <summary>Whether the encryption handler is supported (Standard).</summary>
    public bool HandlerSupported { get; private set; } = true;

    /// <summary>The cipher in effect.</summary>
    public EncryptionAlgorithm Algorithm { get; private set; } = EncryptionAlgorithm.None;

    /// <summary>The security-handler revision.</summary>
    public int EncryptionRevision { get; private set; }

    /// <summary>Which password authenticated, or Failed / NotRequired.</summary>
    public AuthenticationResult Authentication { get; private set; } = AuthenticationResult.NotRequired;

    /// <summary>Whether metadata is encrypted.</summary>
    public bool EncryptMetadata { get; private set; } = true;

    /// <summary>The permission flags (<c>/P</c>).</summary>
    public int Permissions { get; private set; }

    /// <summary>Opens a PDF from a stream, resolves its cross-reference, and configures decryption.</summary>
    public static PdfFile Open(Stream stream, string? password = null)
    {
        var source = PdfByteSource.Create(stream);
        try
        {
            var file = new PdfFile(source);
            file.BuildCrossReference();
            file.SetupEncryption(password);
            return file;
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>Resolves <paramref name="value"/> if it is an indirect reference; otherwise returns it unchanged.</summary>
    public PdfObject Resolve(PdfObject value)
        => value is PdfReference reference ? GetObject(reference.ObjectNumber) : value;

    /// <summary>Materializes the object with the given number, or <see cref="PdfNull"/> if unknown/free.</summary>
    public PdfObject GetObject(int number)
    {
        if (_objectCache.TryGetValue(number, out var cached)) return cached;

        PdfObject result = PdfNull.Instance;
        if (_xref!.TryGetValue(number, out var entry))
        {
            switch (entry.Type)
            {
                case XrefEntryType.InUse:
                    result = LoadObjectAt(entry.Field2);
                    break;
                case XrefEntryType.Compressed:
                    result = LoadFromObjectStream((int)entry.Field2, entry.Field3);
                    break;
                case XrefEntryType.Free:
                default:
                    result = PdfNull.Instance;
                    break;
            }
        }

        _objectCache[number] = result;
        return result;
    }

    private void BuildCrossReference()
    {
        long start = FindStartXref();
        var table = new Dictionary<int, XrefEntry>();
        PdfDictionary? trailer = null;
        var visited = new HashSet<long>();

        long? next = start;
        while (next.HasValue && next.Value >= 0 && next.Value < _source.Length && visited.Add(next.Value))
        {
            XrefSection section = ReadSectionAt(next.Value);
            Merge(table, section.Entries);
            trailer ??= section.Trailer;

            if (section.XRefStm.HasValue && visited.Add(section.XRefStm.Value))
            {
                XrefSection hybrid = ReadSectionAt(section.XRefStm.Value);
                Merge(table, hybrid.Entries);
            }

            next = section.Prev;
        }

        if (trailer is null) throw new PdfSyntaxException("No trailer dictionary was found.");
        _xref = table;
        _trailer = trailer;
    }

    private static void Merge(Dictionary<int, XrefEntry> target, Dictionary<int, XrefEntry> source)
    {
        // Newest entries are merged first; never overwrite an already-known object.
        foreach (var pair in source)
        {
            if (!target.ContainsKey(pair.Key)) target[pair.Key] = pair.Value;
        }
    }

    private long FindStartXref()
    {
        long pos = _source.LastIndexOf(StartXrefKeyword, _source.Length, maxScan: 2048);
        if (pos < 0) throw new PdfSyntaxException("Missing 'startxref'.");

        int windowLen = (int)Math.Min(2048, _source.Length - pos);
        byte[] window = _source.ReadBytes(pos, windowLen);
        var lexer = new PdfLexer(window, 0, window.Length);
        lexer.Next(); // 'startxref'
        Token offset = lexer.Next();
        if (offset.Type != TokenType.Integer) throw new PdfSyntaxException("Invalid 'startxref' offset.");
        return offset.IntValue;
    }

    private XrefSection ReadSectionAt(long position)
    {
        int peekLen = (int)Math.Min(16, _source.Length - position);
        byte[] peek = _source.ReadBytes(position, peekLen);
        var lexer = new PdfLexer(peek, 0, peek.Length);
        Token first = lexer.Next();
        return first.Type == TokenType.Keyword && first.Text == "xref"
            ? ReadClassicSection(position)
            : ReadXrefStreamSection(position);
    }

    private XrefSection ReadClassicSection(long position)
    {
        long trailerPos = _source.IndexOf(TrailerKeyword, position);
        if (trailerPos < 0) throw new PdfSyntaxException("Classic cross-reference table without a trailer.");

        byte[] tableBytes = _source.ReadBytes(position, (int)(trailerPos - position));
        var lexer = new PdfLexer(tableBytes, 0, tableBytes.Length);
        lexer.Next(); // 'xref'

        var entries = new Dictionary<int, XrefEntry>();
        while (true)
        {
            Token startToken = lexer.Next();
            if (startToken.Type != TokenType.Integer) break;
            Token countToken = lexer.Next();
            if (countToken.Type != TokenType.Integer) throw new PdfSyntaxException("Malformed xref subsection header.");

            long startNumber = startToken.IntValue;
            long count = countToken.IntValue;
            for (long i = 0; i < count; i++)
            {
                Token offsetToken = lexer.Next();
                Token genToken = lexer.Next();
                Token typeToken = lexer.Next();
                if (offsetToken.Type != TokenType.Integer || genToken.Type != TokenType.Integer || typeToken.Type != TokenType.Keyword)
                {
                    throw new PdfSyntaxException("Malformed xref entry.");
                }

                int objectNumber = (int)(startNumber + i);
                if (entries.ContainsKey(objectNumber)) continue;
                entries[objectNumber] = typeToken.Text == "n"
                    ? XrefEntry.InUse(offsetToken.IntValue, (int)genToken.IntValue)
                    : XrefEntry.Free();
            }
        }

        byte[] trailerWindow = _source.ReadBytes(trailerPos, (int)Math.Min(8192, _source.Length - trailerPos));
        var trailerLexer = new PdfLexer(trailerWindow, 0, trailerWindow.Length);
        trailerLexer.Next(); // 'trailer'
        PdfObject trailerObj = new PdfObjectParser(trailerLexer).ParseValue();
        if (trailerObj is not PdfDictionary trailer) throw new PdfSyntaxException("Trailer is not a dictionary.");

        return new XrefSection(entries, trailer, GetIntOrNull(trailer, "Prev"), GetIntOrNull(trailer, "XRefStm"));
    }

    private XrefSection ReadXrefStreamSection(long position)
    {
        if (LoadObjectAt(position) is not PdfStream stream) throw new PdfSyntaxException("Expected a cross-reference stream.");

        PdfDictionary dict = stream.Dictionary;
        byte[] data = StreamDecoder.Decode(stream);

        if (dict["W"] is not PdfArray widths || widths.Count < 3) throw new PdfSyntaxException("Cross-reference stream missing /W.");
        int w0 = WidthAt(widths, 0);
        int w1 = WidthAt(widths, 1);
        int w2 = WidthAt(widths, 2);
        int recordLength = w0 + w1 + w2;
        if (recordLength <= 0) throw new PdfSyntaxException("Invalid /W widths in cross-reference stream.");

        var subsections = new List<long[]>();
        if (dict["Index"] is PdfArray index)
        {
            for (int i = 0; i + 1 < index.Count; i += 2)
            {
                subsections.Add(new[] { AsLong(index[i]), AsLong(index[i + 1]) });
            }
        }
        else
        {
            subsections.Add(new[] { 0L, GetIntOrNull(dict, "Size") ?? 0L });
        }

        var entries = new Dictionary<int, XrefEntry>();
        int pos = 0;
        foreach (long[] sub in subsections)
        {
            long startNumber = sub[0];
            long count = sub[1];
            for (long i = 0; i < count && pos + recordLength <= data.Length; i++)
            {
                long type = w0 == 0 ? 1 : ReadBigEndian(data, pos, w0);
                pos += w0;
                long field2 = ReadBigEndian(data, pos, w1);
                pos += w1;
                long field3 = ReadBigEndian(data, pos, w2);
                pos += w2;

                int objectNumber = (int)(startNumber + i);
                if (entries.ContainsKey(objectNumber)) continue;
                switch (type)
                {
                    case 1:
                        entries[objectNumber] = XrefEntry.InUse(field2, (int)field3);
                        break;
                    case 2:
                        entries[objectNumber] = XrefEntry.Compressed(field2, (int)field3);
                        break;
                    default:
                        entries[objectNumber] = XrefEntry.Free();
                        break;
                }
            }
        }

        return new XrefSection(entries, dict, GetIntOrNull(dict, "Prev"), null);
    }

    private PdfObject LoadObjectAt(long offset)
    {
        long endObj = _source.IndexOf(EndObjKeyword, offset);
        long windowEnd = endObj >= 0 ? endObj + EndObjKeyword.Length : _source.Length;
        int windowLength = (int)(windowEnd - offset);
        if (windowLength <= 0) throw new PdfSyntaxException($"Cannot read object at offset {offset}.");

        byte[] window = _source.ReadBytes(offset, windowLength);
        var lexer = new PdfLexer(window, 0, window.Length);
        Token number = lexer.Next();
        Token generation = lexer.Next();
        Token obj = lexer.Next();
        if (number.Type != TokenType.Integer || generation.Type != TokenType.Integer
            || obj.Type != TokenType.Keyword || obj.Text != "obj")
        {
            throw new PdfSyntaxException($"Malformed indirect object header at offset {offset}.");
        }

        PdfObject value = new PdfObjectParser(lexer).ParseValue();
        int objectNumber = (int)number.IntValue;
        if (_handler is not null && objectNumber != _encryptObjectNumber)
        {
            value = DecryptObject(value, objectNumber, (int)generation.IntValue);
        }
        return value;
    }

    private void SetupEncryption(string? password)
    {
        PdfObject? encryptObj = _trailer!["Encrypt"];
        if (encryptObj is null) return;

        IsEncrypted = true;
        if (encryptObj is PdfReference encReference) _encryptObjectNumber = encReference.ObjectNumber;

        if (Resolve(encryptObj) is not PdfDictionary enc)
        {
            HandlerSupported = false;
            return;
        }
        if ((enc["Filter"] as PdfName)?.Value != "Standard")
        {
            HandlerSupported = false; // e.g. public-key handler — out of scope
            return;
        }

        int v = IntOf(enc, "V", 0);
        int r = IntOf(enc, "R", 0);
        int lengthBits = IntOf(enc, "Length", 40);
        EncryptionAlgorithm algorithm = DetermineAlgorithm(enc, v, r);

        Algorithm = algorithm;
        EncryptionRevision = r;
        Permissions = IntOf(enc, "P", 0);
        EncryptMetadata = !(enc["EncryptMetadata"] is PdfBoolean meta) || meta.Value;

        int keyLengthBytes = algorithm == EncryptionAlgorithm.Aes256 ? 32 : lengthBits / 8;
        _handler = new StandardSecurityHandler(
            algorithm, r, keyLengthBytes,
            StringBytes(enc, "O"), StringBytes(enc, "U"), StringBytes(enc, "OE"), StringBytes(enc, "UE"),
            Permissions, FirstIdBytes(), EncryptMetadata, password);
        Authentication = _handler.Authentication;
    }

    private EncryptionAlgorithm DetermineAlgorithm(PdfDictionary enc, int v, int r)
    {
        if (v >= 5 || r >= 5) return EncryptionAlgorithm.Aes256;
        if (v == 4)
        {
            switch (CryptFilterMethod(enc))
            {
                case "AESV2": return EncryptionAlgorithm.Aes128;
                case "AESV3": return EncryptionAlgorithm.Aes256;
                default: return EncryptionAlgorithm.Rc4;
            }
        }
        return EncryptionAlgorithm.Rc4;
    }

    private string CryptFilterMethod(PdfDictionary enc)
    {
        string name = (enc["StmF"] as PdfName)?.Value ?? "StdCF";
        if (Resolve(enc["CF"] ?? PdfNull.Instance) is PdfDictionary cf
            && Resolve(cf[name] ?? PdfNull.Instance) is PdfDictionary filter
            && filter["CFM"] is PdfName cfm)
        {
            return cfm.Value;
        }
        return "V2";
    }

    private PdfObject DecryptObject(PdfObject value, int objectNumber, int generation)
    {
        switch (value)
        {
            case PdfString s:
                return new PdfString(_handler!.DecryptString(s.ToArray(), objectNumber, generation), s.IsHexString);
            case PdfArray a:
            {
                var items = new List<PdfObject>(a.Count);
                for (int i = 0; i < a.Count; i++) items.Add(DecryptObject(a[i], objectNumber, generation));
                return new PdfArray(items);
            }
            case PdfDictionary d:
                return DecryptDictionary(d, objectNumber, generation);
            case PdfStream stream:
            {
                PdfDictionary dict = DecryptDictionary(stream.Dictionary, objectNumber, generation);
                byte[] raw = _handler!.DecryptStream(stream.GetRawBytes(), objectNumber, generation);
                return new PdfStream(dict, raw);
            }
            default:
                return value;
        }
    }

    private PdfDictionary DecryptDictionary(PdfDictionary dictionary, int objectNumber, int generation)
    {
        var map = new Dictionary<string, PdfObject>(StringComparer.Ordinal);
        foreach (string key in dictionary.Keys)
        {
            PdfObject? entry = dictionary[key];
            if (entry is not null) map[key] = DecryptObject(entry, objectNumber, generation);
        }
        return new PdfDictionary(map);
    }

    private static int IntOf(PdfDictionary dict, string key, int fallback)
        => dict[key] is PdfNumber n ? (int)n.AsInt64 : fallback;

    private static byte[] StringBytes(PdfDictionary dict, string key)
        => dict[key] is PdfString s ? s.ToArray() : Array.Empty<byte>();

    private byte[] FirstIdBytes()
        => _trailer!["ID"] is PdfArray id && id.Count > 0 && id[0] is PdfString s ? s.ToArray() : Array.Empty<byte>();

    private PdfObject LoadFromObjectStream(int objectStreamNumber, int index)
    {
        ObjStmData? data = GetObjectStream(objectStreamNumber);
        if (data is null || index < 0 || index >= data.Entries.Length) return PdfNull.Instance;

        int start = data.First + data.Entries[index].Offset;
        if (start < 0 || start > data.Bytes.Length) return PdfNull.Instance;
        return new PdfObjectParser(data.Bytes, start, data.Bytes.Length).ParseValue();
    }

    private ObjStmData? GetObjectStream(int objectStreamNumber)
    {
        if (_objectStreamCache.TryGetValue(objectStreamNumber, out var cached)) return cached;

        ObjStmData? data = null;
        if (GetObject(objectStreamNumber) is PdfStream stream)
        {
            byte[] decoded = StreamDecoder.Decode(stream);
            int count = (int)(GetIntOrNull(stream.Dictionary, "N") ?? 0);
            int first = (int)(GetIntOrNull(stream.Dictionary, "First") ?? 0);

            var headerLexer = new PdfLexer(decoded, 0, Math.Min(first, decoded.Length));
            var entries = new ObjStmEntry[count];
            for (int i = 0; i < count; i++)
            {
                Token num = headerLexer.Next();
                Token off = headerLexer.Next();
                if (num.Type != TokenType.Integer || off.Type != TokenType.Integer)
                {
                    throw new PdfSyntaxException("Malformed object stream header.");
                }
                entries[i] = new ObjStmEntry((int)num.IntValue, (int)off.IntValue);
            }

            data = new ObjStmData(decoded, first, entries);
        }

        _objectStreamCache[objectStreamNumber] = data;
        return data;
    }

    private static int WidthAt(PdfArray widths, int i) => widths[i] is PdfInteger v ? (int)v.Value : 0;

    private static long AsLong(PdfObject value) => value is PdfInteger v ? v.Value : 0L;

    private static long? GetIntOrNull(PdfDictionary dict, string key)
        => dict[key] is PdfInteger value ? value.Value : (long?)null;

    private static long ReadBigEndian(byte[] data, int offset, int width)
    {
        long value = 0;
        for (int i = 0; i < width; i++) value = (value << 8) | data[offset + i];
        return value;
    }

    /// <summary>Releases the underlying byte source.</summary>
    public void Dispose() => _source.Dispose();

    private readonly struct XrefSection
    {
        public XrefSection(Dictionary<int, XrefEntry> entries, PdfDictionary trailer, long? prev, long? xrefStm)
        {
            Entries = entries;
            Trailer = trailer;
            Prev = prev;
            XRefStm = xrefStm;
        }

        public Dictionary<int, XrefEntry> Entries { get; }

        public PdfDictionary Trailer { get; }

        public long? Prev { get; }

        public long? XRefStm { get; }
    }

    private readonly struct ObjStmEntry
    {
        public ObjStmEntry(int number, int offset)
        {
            Number = number;
            Offset = offset;
        }

        public int Number { get; }

        public int Offset { get; }
    }

    private sealed class ObjStmData
    {
        public ObjStmData(byte[] bytes, int first, ObjStmEntry[] entries)
        {
            Bytes = bytes;
            First = first;
            Entries = entries;
        }

        public byte[] Bytes { get; }

        public int First { get; }

        public ObjStmEntry[] Entries { get; }
    }
}
