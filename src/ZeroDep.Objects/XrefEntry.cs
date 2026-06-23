namespace ZeroDep.Objects;

/// <summary>The kind of cross-reference entry for an object.</summary>
internal enum XrefEntryType
{
    /// <summary>A free (deleted) object.</summary>
    Free = 0,
    /// <summary>An uncompressed object located at a byte offset.</summary>
    InUse = 1,
    /// <summary>An object stored inside an object stream (ISO 32000-2 §7.5.7).</summary>
    Compressed = 2,
}

/// <summary>A single cross-reference entry locating one object.</summary>
internal readonly struct XrefEntry
{
    private XrefEntry(XrefEntryType type, long field2, int field3)
    {
        Type = type;
        Field2 = field2;
        Field3 = field3;
    }

    /// <summary>The entry kind.</summary>
    public XrefEntryType Type { get; }

    /// <summary>Byte offset (InUse) or containing object-stream number (Compressed).</summary>
    public long Field2 { get; }

    /// <summary>Generation (InUse) or index within the object stream (Compressed).</summary>
    public int Field3 { get; }

    public static XrefEntry Free() => new XrefEntry(XrefEntryType.Free, 0, 0);

    public static XrefEntry InUse(long offset, int generation) => new XrefEntry(XrefEntryType.InUse, offset, generation);

    public static XrefEntry Compressed(long objectStreamNumber, int indexInStream)
        => new XrefEntry(XrefEntryType.Compressed, objectStreamNumber, indexInStream);
}
