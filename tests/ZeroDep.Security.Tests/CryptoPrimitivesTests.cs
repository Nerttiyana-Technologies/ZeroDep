using System.Security.Cryptography;
using System.Text;
using Xunit;
using ZeroDep.Security;

namespace ZeroDep.Security.Tests;

public sealed class CryptoPrimitivesTests
{
    [Fact]
    public void Rc4MatchesKnownVector()
    {
        byte[] key = Encoding.ASCII.GetBytes("Key");
        byte[] plaintext = Encoding.ASCII.GetBytes("Plaintext");
        byte[] expected = { 0xBB, 0xF3, 0x16, 0xE8, 0xD9, 0x40, 0xAF, 0x0A, 0xD3 };

        byte[] cipher = Rc4.Transform(key, plaintext);
        Assert.Equal(expected, cipher);

        // RC4 is symmetric.
        Assert.Equal(plaintext, Rc4.Transform(key, cipher));
    }

    [Fact]
    public void AesCbcWithIvPrefixRoundTrips()
    {
        byte[] key = new byte[16];
        for (int i = 0; i < key.Length; i++) key[i] = (byte)(i + 1);
        byte[] plaintext = Encoding.ASCII.GetBytes("ZeroDep AES round-trip payload");

        byte[] ivPrefixed = EncryptWithIvPrefix(key, plaintext);
        byte[] decrypted = AesCipher.DecryptWithIvPrefix(key, ivPrefixed);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void AesNoPaddingRoundTrips()
    {
        byte[] key = new byte[32];
        byte[] iv = new byte[16];
        for (int i = 0; i < 32; i++) key[i] = (byte)i;
        byte[] block = new byte[32]; // exact block multiple, no padding
        for (int i = 0; i < 32; i++) block[i] = (byte)(255 - i);

        byte[] enc = AesCipher.EncryptNoPadding(key, iv, block);
        byte[] dec = AesCipher.DecryptNoPadding(key, iv, enc);
        Assert.Equal(block, dec);
    }

    private static byte[] EncryptWithIvPrefix(byte[] key, byte[] plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var encryptor = aes.CreateEncryptor();
        byte[] cipher = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);

        var result = new byte[16 + cipher.Length];
        System.Array.Copy(aes.IV, 0, result, 0, 16);
        System.Array.Copy(cipher, 0, result, 16, cipher.Length);
        return result;
    }
}
