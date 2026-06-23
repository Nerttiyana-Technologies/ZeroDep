using System;
using System.IO;
using ZeroDep.Abstractions;

namespace ZeroDep.Validation;

/// <summary>
/// Pre-flight integrity checks and parse-failure classification for the reject-don't-repair policy
/// (ISO 32000-2 §7.5). Returns a <see cref="RejectionReason"/> when a document should be rejected.
/// </summary>
public static class PdfValidator
{
    private const int HeaderScan = 1024;
    private const int TrailerScan = 2048;

    /// <summary>
    /// Cheap structural pre-flight: confirms the <c>%PDF-</c> header and a trailing <c>%%EOF</c>.
    /// Returns null when the document passes; otherwise the reason to reject. Leaves the stream at 0.
    /// </summary>
    public static RejectionReason? Preflight(Stream stream)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        if (!stream.CanSeek) return null; // can't cheaply pre-flight; let parsing decide

        long length = stream.Length;
        if (length < 5)
        {
            stream.Position = 0;
            return RejectionReason.MissingHeader;
        }

        int headLen = (int)Math.Min(HeaderScan, length);
        byte[] head = Read(stream, 0, headLen);
        if (IndexOf(head, "%PDF-") < 0)
        {
            stream.Position = 0;
            return RejectionReason.MissingHeader;
        }

        int tailLen = (int)Math.Min(TrailerScan, length);
        byte[] tail = Read(stream, length - tailLen, tailLen);
        stream.Position = 0;
        return IndexOf(tail, "%%EOF") < 0 ? RejectionReason.MissingEof : (RejectionReason?)null;
    }

    /// <summary>Maps a parse-failure message to a rejection reason.</summary>
    public static RejectionReason Classify(string message)
    {
        string m = (message ?? string.Empty).ToLowerInvariant();
        if (m.Contains("password") || m.Contains("authentication")) return RejectionReason.EncryptedPasswordRequired;
        if (m.Contains("encrypt") || m.Contains("decrypt")) return RejectionReason.EncryptionUnsupported;
        if (m.Contains("startxref") || m.Contains("xref") || m.Contains("trailer")) return RejectionReason.XrefUnresolvable;
        if (m.Contains("catalog") || m.Contains("page tree") || m.Contains("/root") || m.Contains("/pages")) return RejectionReason.CatalogUnreachable;
        if (m.Contains("endstream") || m.Contains("truncat") || m.Contains("end of input")) return RejectionReason.TruncatedStream;
        return RejectionReason.MalformedObject;
    }

    private static byte[] Read(Stream stream, long position, int count)
    {
        stream.Position = position;
        var buffer = new byte[count];
        int total = 0;
        while (total < count)
        {
            int n = stream.Read(buffer, total, count - total);
            if (n == 0) break;
            total += n;
        }
        if (total == count) return buffer;
        var trimmed = new byte[total];
        Array.Copy(buffer, trimmed, total);
        return trimmed;
    }

    private static int IndexOf(byte[] haystack, string needle)
    {
        int last = haystack.Length - needle.Length;
        for (int i = 0; i <= last; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != (byte)needle[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }
}
