namespace ZeroDep.Abstractions;

/// <summary>The cipher used by a PDF standard security handler.</summary>
public enum EncryptionAlgorithm
{
    /// <summary>The document is not encrypted.</summary>
    None = 0,

    /// <summary>RC4 (security handler revisions 2–4; deprecated in PDF 2.0).</summary>
    Rc4,

    /// <summary>AES-128 in CBC mode (crypt filter method AESV2).</summary>
    Aes128,

    /// <summary>AES-256 in CBC mode (crypt filter method AESV3).</summary>
    Aes256,
}
