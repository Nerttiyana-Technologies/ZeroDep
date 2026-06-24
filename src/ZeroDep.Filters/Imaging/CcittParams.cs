namespace ZeroDep.Filters;

/// <summary>
/// Decode parameters for <c>/CCITTFaxDecode</c> (ISO 32000-2 §7.4.6 / ITU-T T.4 &amp; T.6), taken from the
/// image stream's <c>/DecodeParms</c> dictionary (with PDF defaults).
/// </summary>
public sealed class CcittParams
{
    /// <summary>
    /// Coding scheme selector: <c>K &lt; 0</c> = pure two-dimensional (Group 4, T.6); <c>K = 0</c> =
    /// pure one-dimensional (Group 3 1D); <c>K &gt; 0</c> = mixed one/two-dimensional (Group 3 2D).
    /// Default 0.
    /// </summary>
    public int K { get; init; }

    /// <summary>Pixels per row (<c>/Columns</c>). Default 1728.</summary>
    public int Columns { get; init; } = 1728;

    /// <summary>Number of rows (<c>/Rows</c>); 0 means "decode until end of data". Default 0.</summary>
    public int Rows { get; init; }

    /// <summary>
    /// When true, 1 bits are black and 0 bits white (the reverse of the normal PDF convention).
    /// Default false. ZeroDep decodes runs directly, so this only controls the final sample polarity.
    /// </summary>
    public bool BlackIs1 { get; init; }

    /// <summary>When true, each row's encoded data is padded to a byte boundary (<c>/EncodedByteAlign</c>). Default false.</summary>
    public bool EncodedByteAlign { get; init; }

    /// <summary>When true, the stream is expected to carry an end-of-block (EOFB/RTC) pattern (<c>/EndOfBlock</c>). Default true.</summary>
    public bool EndOfBlock { get; init; } = true;

    /// <summary>When true, encoded rows are preceded by an end-of-line (EOL) bit pattern (<c>/EndOfLine</c>). Default false.</summary>
    public bool EndOfLine { get; init; }
}
