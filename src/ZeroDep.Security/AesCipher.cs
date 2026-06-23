using System;
using System.IO;
using System.Security.Cryptography;

namespace ZeroDep.Security;

/// <summary>AES-CBC helpers (BCL <see cref="Aes"/>) for PDF AESV2/AESV3 decryption and the R6 hash.</summary>
internal static class AesCipher
{
    /// <summary>
    /// Decrypts AESV2/AESV3 stream data: the first 16 bytes are the IV, the remainder is
    /// CBC-encrypted with PKCS#7 padding.
    /// </summary>
    public static byte[] DecryptWithIvPrefix(byte[] key, byte[] data)
    {
        if (data.Length <= 16) return Array.Empty<byte>();

        var iv = new byte[16];
        Array.Copy(data, 0, iv, 0, 16);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        try
        {
            return decryptor.TransformFinalBlock(data, 16, data.Length - 16);
        }
        catch (CryptographicException)
        {
            return Array.Empty<byte>(); // wrong key / corrupt data
        }
    }

    /// <summary>CBC decryption with an explicit IV and no padding (used to unwrap UE/OE in revision 6).</summary>
    public static byte[] DecryptNoPadding(byte[] key, byte[] iv, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(data, 0, data.Length);
    }

    /// <summary>CBC encryption with an explicit IV and no padding (used inside the revision-6 hash).</summary>
    public static byte[] EncryptNoPadding(byte[] key, byte[] iv, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(data, 0, data.Length);
    }
}
