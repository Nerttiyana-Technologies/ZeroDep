namespace ZeroDep.Abstractions;

/// <summary>Machine-readable reason a document was rejected by integrity validation.</summary>
public enum RejectionReason
{
    /// <summary>No rejection; the document was accepted.</summary>
    None = 0,

    /// <summary>No <c>%PDF-x.y</c> signature was found in the leading bytes.</summary>
    MissingHeader,

    /// <summary>No <c>%%EOF</c> marker or no resolvable <c>startxref</c>.</summary>
    MissingEof,

    /// <summary>Neither the cross-reference table nor stream could be resolved.</summary>
    XrefUnresolvable,

    /// <summary>The document catalog or page tree could not be reached.</summary>
    CatalogUnreachable,

    /// <summary>A stream ended before its <c>endstream</c>, or the file is truncated.</summary>
    TruncatedStream,

    /// <summary>An object, dictionary, or array was unbalanced or unterminated.</summary>
    MalformedObject,

    /// <summary>The document is encrypted with a handler ZeroDep does not support (e.g. public-key).</summary>
    EncryptionUnsupported,

    /// <summary>
    /// The document is encrypted with a supported handler, but no supplied or default (empty)
    /// password authenticated it — a password is required to open it.
    /// </summary>
    EncryptedPasswordRequired,
}
