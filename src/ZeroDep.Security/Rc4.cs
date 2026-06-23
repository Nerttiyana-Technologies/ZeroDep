using System;

namespace ZeroDep.Security;

/// <summary>The RC4 stream cipher (symmetric) used by PDF standard security revisions 2–4.</summary>
internal static class Rc4
{
    /// <summary>Encrypts or decrypts <paramref name="data"/> with <paramref name="key"/> (RC4 is symmetric).</summary>
    public static byte[] Transform(byte[] key, byte[] data)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (key.Length == 0) return (byte[])data.Clone();

        var s = new byte[256];
        for (int i = 0; i < 256; i++) s[i] = (byte)i;

        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        var output = new byte[data.Length];
        int a = 0, b = 0;
        for (int k = 0; k < data.Length; k++)
        {
            a = (a + 1) & 0xFF;
            b = (b + s[a]) & 0xFF;
            (s[a], s[b]) = (s[b], s[a]);
            output[k] = (byte)(data[k] ^ s[(s[a] + s[b]) & 0xFF]);
        }
        return output;
    }
}
