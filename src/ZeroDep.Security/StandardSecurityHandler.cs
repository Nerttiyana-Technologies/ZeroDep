using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ZeroDep.Abstractions;

namespace ZeroDep.Security;

/// <summary>
/// The PDF standard security handler (ISO 32000-2 §7.6.4). Derives the file encryption key from a
/// supplied or empty password, authenticates against /U or /O, and decrypts strings and streams.
/// Operates on primitive /Encrypt parameters so it has no dependency on the object model.
/// </summary>
internal sealed class StandardSecurityHandler
{
    private static readonly byte[] Padding =
    {
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    };

    private static readonly byte[] Salt = { 0x73, 0x41, 0x6C, 0x54 }; // "sAlT"

    private readonly int _revision;
    private readonly int _keyLengthBytes;
    private readonly byte[] _o;
    private readonly byte[] _u;
    private readonly byte[] _oe;
    private readonly byte[] _ue;
    private readonly int _permissions;
    private readonly byte[] _idFirst;
    private readonly bool _encryptMetadata;

    private byte[]? _fileKey;

    public StandardSecurityHandler(
        EncryptionAlgorithm algorithm, int revision, int keyLengthBytes,
        byte[] o, byte[] u, byte[] oe, byte[] ue, int permissions, byte[] idFirst,
        bool encryptMetadata, string? password)
    {
        Algorithm = algorithm;
        _revision = revision;
        _keyLengthBytes = keyLengthBytes <= 0 ? 5 : keyLengthBytes;
        _o = o;
        _u = u;
        _oe = oe;
        _ue = ue;
        _permissions = permissions;
        _idFirst = idFirst;
        _encryptMetadata = encryptMetadata;
        Authentication = Authenticate(password ?? string.Empty);
    }

    /// <summary>The cipher in effect.</summary>
    public EncryptionAlgorithm Algorithm { get; }

    /// <summary>The security-handler revision.</summary>
    public int Revision => _revision;

    /// <summary>Which password authenticated, or Failed.</summary>
    public AuthenticationResult Authentication { get; }

    /// <summary>Decrypts a stream's raw bytes for the given object number/generation.</summary>
    public byte[] DecryptStream(byte[] data, int objectNumber, int generation) => Decrypt(data, objectNumber, generation);

    /// <summary>Decrypts a string's bytes for the given object number/generation.</summary>
    public byte[] DecryptString(byte[] data, int objectNumber, int generation) => Decrypt(data, objectNumber, generation);

    private byte[] Decrypt(byte[] data, int objectNumber, int generation)
    {
        if (_fileKey is null || data.Length == 0) return data;

        if (Algorithm == EncryptionAlgorithm.Aes256)
        {
            return AesCipher.DecryptWithIvPrefix(_fileKey, data);
        }

        byte[] objectKey = ObjectKey(objectNumber, generation);
        return Algorithm == EncryptionAlgorithm.Aes128
            ? AesCipher.DecryptWithIvPrefix(objectKey, data)
            : Rc4.Transform(objectKey, data);
    }

    private byte[] ObjectKey(int objectNumber, int generation)
    {
        using var md5 = MD5.Create();
        using var ms = new MemoryStream();
        ms.Write(_fileKey!, 0, _fileKey!.Length);
        ms.WriteByte((byte)(objectNumber & 0xFF));
        ms.WriteByte((byte)((objectNumber >> 8) & 0xFF));
        ms.WriteByte((byte)((objectNumber >> 16) & 0xFF));
        ms.WriteByte((byte)(generation & 0xFF));
        ms.WriteByte((byte)((generation >> 8) & 0xFF));
        if (Algorithm == EncryptionAlgorithm.Aes128) ms.Write(Salt, 0, Salt.Length);

        byte[] hash = md5.ComputeHash(ms.ToArray());
        int n = Math.Min(_fileKey!.Length + 5, 16);
        var key = new byte[n];
        Array.Copy(hash, key, n);
        return key;
    }

    private AuthenticationResult Authenticate(string password)
        => _revision >= 5 ? AuthenticateR6(password) : AuthenticateLegacy(password);

    // ---- Revisions 2–4 (MD5/RC4 key derivation) ----

    private AuthenticationResult AuthenticateLegacy(string password)
    {
        byte[] pw = Latin1(password);

        byte[] userKey = ComputeKeyLegacy(pw);
        if (UserKeyMatches(userKey)) { _fileKey = userKey; return AuthenticationResult.UserPassword; }

        byte[] recoveredUserPw = RecoverUserPasswordFromOwner(pw);
        byte[] ownerKey = ComputeKeyLegacy(recoveredUserPw);
        if (UserKeyMatches(ownerKey)) { _fileKey = ownerKey; return AuthenticationResult.OwnerPassword; }

        return AuthenticationResult.Failed;
    }

    private byte[] ComputeKeyLegacy(byte[] passwordBytes)
    {
        using var md5 = MD5.Create();
        using var ms = new MemoryStream();
        ms.Write(Pad(passwordBytes), 0, 32);
        ms.Write(_o, 0, Math.Min(32, _o.Length));
        ms.Write(Int32Le(_permissions), 0, 4);
        ms.Write(_idFirst, 0, _idFirst.Length);
        if (_revision >= 4 && !_encryptMetadata) ms.Write(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, 0, 4);

        byte[] hash = md5.ComputeHash(ms.ToArray());
        int n = _revision == 2 ? 5 : _keyLengthBytes;
        if (_revision >= 3)
        {
            for (int i = 0; i < 50; i++) hash = md5.ComputeHash(hash, 0, n);
        }
        var key = new byte[n];
        Array.Copy(hash, key, n);
        return key;
    }

    private bool UserKeyMatches(byte[] key)
    {
        if (_revision == 2)
        {
            byte[] computed = Rc4.Transform(key, Padding);
            return FirstBytesEqual(computed, _u, 32);
        }

        using var md5 = MD5.Create();
        using var ms = new MemoryStream();
        ms.Write(Padding, 0, 32);
        ms.Write(_idFirst, 0, _idFirst.Length);
        byte[] hash = md5.ComputeHash(ms.ToArray());

        byte[] u = Rc4.Transform(key, hash);
        for (int i = 1; i <= 19; i++)
        {
            byte[] k2 = new byte[key.Length];
            for (int t = 0; t < key.Length; t++) k2[t] = (byte)(key[t] ^ i);
            u = Rc4.Transform(k2, u);
        }
        return FirstBytesEqual(u, _u, 16);
    }

    private byte[] RecoverUserPasswordFromOwner(byte[] ownerPasswordBytes)
    {
        using var md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(Pad(ownerPasswordBytes));
        int n = _revision == 2 ? 5 : _keyLengthBytes;
        if (_revision >= 3)
        {
            for (int i = 0; i < 50; i++) hash = md5.ComputeHash(hash, 0, n);
        }
        var rc4Key = new byte[n];
        Array.Copy(hash, rc4Key, n);

        if (_revision == 2) return Rc4.Transform(rc4Key, _o);

        byte[] result = (byte[])_o.Clone();
        for (int i = 19; i >= 0; i--)
        {
            byte[] k2 = new byte[n];
            for (int t = 0; t < n; t++) k2[t] = (byte)(rc4Key[t] ^ i);
            result = Rc4.Transform(k2, result);
        }
        return result;
    }

    // ---- Revision 6 (AES-256, SHA-2 hardened hash) ----

    private AuthenticationResult AuthenticateR6(string password)
    {
        byte[] pw = Utf8Truncated(password, 127);

        if (_u.Length >= 48)
        {
            byte[] uHash = Slice(_u, 0, 32);
            byte[] uValidationSalt = Slice(_u, 32, 8);
            byte[] uKeySalt = Slice(_u, 40, 8);
            if (FirstBytesEqual(Hash2B(pw, uValidationSalt, Array.Empty<byte>()), uHash, 32))
            {
                byte[] intermediate = Hash2B(pw, uKeySalt, Array.Empty<byte>());
                _fileKey = AesCipher.DecryptNoPadding(intermediate, new byte[16], _ue);
                return AuthenticationResult.UserPassword;
            }
        }

        if (_o.Length >= 48 && _u.Length >= 48)
        {
            byte[] oHash = Slice(_o, 0, 32);
            byte[] oValidationSalt = Slice(_o, 32, 8);
            byte[] oKeySalt = Slice(_o, 40, 8);
            byte[] u48 = Slice(_u, 0, 48);
            if (FirstBytesEqual(Hash2B(pw, oValidationSalt, u48), oHash, 32))
            {
                byte[] intermediate = Hash2B(pw, oKeySalt, u48);
                _fileKey = AesCipher.DecryptNoPadding(intermediate, new byte[16], _oe);
                return AuthenticationResult.OwnerPassword;
            }
        }

        return AuthenticationResult.Failed;
    }

    private byte[] Hash2B(byte[] password, byte[] salt, byte[] userData)
    {
        byte[] k;
        using (var sha256 = SHA256.Create()) k = sha256.ComputeHash(Concat(password, salt, userData));
        if (_revision < 6) return k;

        int round = 0;
        while (true)
        {
            byte[] block = Concat(password, k, userData);
            byte[] k1 = Repeat(block, 64);
            byte[] e = AesCipher.EncryptNoPadding(Slice(k, 0, 16), Slice(k, 16, 16), k1);

            int mod = 0;
            for (int i = 0; i < 16; i++) mod += e[i];
            mod %= 3;

            k = mod switch
            {
                0 => Sha(256, e),
                1 => Sha(384, e),
                _ => Sha(512, e),
            };

            round++;
            if (round >= 64 && (e[e.Length - 1] & 0xFF) <= round - 32) break;
        }
        return Slice(k, 0, 32);
    }

    private static byte[] Sha(int bits, byte[] data)
    {
        using HashAlgorithm h = bits == 256 ? SHA256.Create() : bits == 384 ? (HashAlgorithm)SHA384.Create() : SHA512.Create();
        return h.ComputeHash(data);
    }

    // ---- helpers ----

    private static byte[] Pad(byte[] pw)
    {
        var result = new byte[32];
        int n = Math.Min(pw.Length, 32);
        Array.Copy(pw, result, n);
        Array.Copy(Padding, 0, result, n, 32 - n);
        return result;
    }

    private static byte[] Latin1(string s)
    {
        var bytes = new byte[s.Length];
        for (int i = 0; i < s.Length; i++) bytes[i] = (byte)s[i];
        return bytes;
    }

    private static byte[] Utf8Truncated(string s, int max)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        if (bytes.Length <= max) return bytes;
        var t = new byte[max];
        Array.Copy(bytes, t, max);
        return t;
    }

    private static byte[] Int32Le(int value)
        => new[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) };

    private static bool FirstBytesEqual(byte[] a, byte[] b, int count)
    {
        if (a.Length < count || b.Length < count) return false;
        for (int i = 0; i < count; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static byte[] Slice(byte[] source, int offset, int length)
    {
        var result = new byte[length];
        Array.Copy(source, offset, result, 0, length);
        return result;
    }

    private static byte[] Concat(byte[] a, byte[] b, byte[] c)
    {
        var result = new byte[a.Length + b.Length + c.Length];
        Array.Copy(a, 0, result, 0, a.Length);
        Array.Copy(b, 0, result, a.Length, b.Length);
        Array.Copy(c, 0, result, a.Length + b.Length, c.Length);
        return result;
    }

    private static byte[] Repeat(byte[] block, int times)
    {
        var result = new byte[block.Length * times];
        for (int i = 0; i < times; i++) Array.Copy(block, 0, result, i * block.Length, block.Length);
        return result;
    }
}
