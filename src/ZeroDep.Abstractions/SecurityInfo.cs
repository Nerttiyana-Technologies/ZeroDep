namespace ZeroDep.Abstractions;

/// <summary>Encryption / access-control status of a document (Feature E).</summary>
public sealed class SecurityInfo
{
    /// <summary>Whether the document is encrypted.</summary>
    public bool IsEncrypted { get; init; }

    /// <summary>Whether the security handler is supported (Standard); false for e.g. public-key.</summary>
    public bool HandlerSupported { get; init; } = true;

    /// <summary>The cipher: None / RC4 / AES-128 / AES-256.</summary>
    public EncryptionAlgorithm Algorithm { get; init; } = EncryptionAlgorithm.None;

    /// <summary>The security-handler revision (2–6), or 0 when not encrypted.</summary>
    public int Revision { get; init; }

    /// <summary>Which password authenticated, or Failed.</summary>
    public AuthenticationResult Authentication { get; init; } = AuthenticationResult.NotRequired;

    /// <summary>Whether document metadata is encrypted (<c>/EncryptMetadata</c>).</summary>
    public bool MetadataEncrypted { get; init; }

    /// <summary>The decoded permission flags (<c>/P</c>).</summary>
    public int Permissions { get; init; }
}
