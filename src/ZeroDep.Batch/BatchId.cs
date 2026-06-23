using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ZeroDep.Batch;

/// <summary>
/// Content-free identifiers for corpus files (ADR data-governance rules). Per-file references are
/// opaque SHA-256 hashes, never paths or names. The published corpus therefore carries no file
/// identity.
/// </summary>
internal static class BatchId
{
    /// <summary>A stable opaque id for a file: the SHA-256 (hex) of its absolute path.</summary>
    public static string ForPath(string absolutePath)
    {
        using SHA256 sha = SHA256.Create();
        return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(absolutePath)));
    }

    /// <summary>The SHA-256 (hex) of a file's bytes, used to detect input changes on resume.</summary>
    public static string ForContent(string path)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return ToHex(sha.ComputeHash(stream));
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
