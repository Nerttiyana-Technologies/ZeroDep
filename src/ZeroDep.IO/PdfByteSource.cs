using System;
using System.IO;

namespace ZeroDep.IO;

/// <summary>
/// Random-access, line-ending-agnostic view over a PDF byte stream.
/// Reads windows on demand so multi-gigabyte files are never loaded whole.
/// Not safe for concurrent use by multiple threads on a single instance.
/// </summary>
internal sealed class PdfByteSource : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly object _gate = new object();
    private bool _disposed;

    private PdfByteSource(Stream stream, bool ownsStream)
    {
        _stream = stream;
        _ownsStream = ownsStream;
    }

    /// <summary>Total length of the underlying data, in bytes.</summary>
    public long Length => _stream.Length;

    /// <summary>
    /// Creates a source over the given stream. Seekable streams are used directly;
    /// non-seekable streams are buffered into memory so they can be random-accessed.
    /// </summary>
    public static PdfByteSource Create(Stream stream)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));

        if (stream.CanSeek)
        {
            return new PdfByteSource(stream, ownsStream: false);
        }

        var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        return new PdfByteSource(buffer, ownsStream: true);
    }

    /// <summary>Reads up to <paramref name="count"/> bytes at <paramref name="position"/>; returns the count actually read.</summary>
    public int ReadAt(long position, byte[] buffer, int offset, int count)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
        if (offset < 0 || count < 0 || offset + count > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(count));

        lock (_gate)
        {
            long len = _stream.Length;
            if (position >= len || count == 0) return 0;

            _stream.Position = position;
            int total = 0;
            while (total < count)
            {
                int n = _stream.Read(buffer, offset + total, count - total);
                if (n == 0) break;
                total += n;
            }
            return total;
        }
    }

    /// <summary>Reads exactly the available bytes at <paramref name="position"/> (up to <paramref name="count"/>), returning a right-sized array.</summary>
    public byte[] ReadBytes(long position, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0) return Array.Empty<byte>();

        var buffer = new byte[count];
        int read = ReadAt(position, buffer, 0, count);
        if (read == count) return buffer;

        var trimmed = new byte[read];
        Array.Copy(buffer, trimmed, read);
        return trimmed;
    }

    /// <summary>Reads a single byte at <paramref name="position"/>, or -1 if out of range.</summary>
    public int ReadByteAt(long position)
    {
        lock (_gate)
        {
            if (position < 0 || position >= _stream.Length) return -1;
            _stream.Position = position;
            return _stream.ReadByte();
        }
    }

    /// <summary>Finds the first occurrence of <paramref name="pattern"/> at or after <paramref name="start"/>; returns -1 if not found.</summary>
    public long IndexOf(byte[] pattern, long start)
    {
        if (pattern is null || pattern.Length == 0) throw new ArgumentException("Pattern must be non-empty.", nameof(pattern));

        long len = Length;
        if (start < 0) start = 0;

        int overlap = pattern.Length - 1;
        const int windowSize = 64 * 1024;
        var buffer = new byte[windowSize + overlap];

        long windowStart = start;
        while (windowStart < len)
        {
            int want = (int)Math.Min(buffer.Length, len - windowStart);
            int got = ReadAt(windowStart, buffer, 0, want);
            if (got < pattern.Length) break;

            int last = got - pattern.Length;
            for (int i = 0; i <= last; i++)
            {
                if (Matches(buffer, i, pattern)) return windowStart + i;
            }

            if (got < want) break;            // reached end of file
            long advance = got - overlap;
            if (advance <= 0) break;          // guard against non-progress
            windowStart += advance;
        }
        return -1;
    }

    /// <summary>
    /// Finds the last occurrence of <paramref name="pattern"/> within the final
    /// <paramref name="maxScan"/> bytes before <paramref name="endExclusive"/>; returns -1 if not found.
    /// Used to locate trailing markers such as <c>%%EOF</c> and <c>startxref</c>.
    /// </summary>
    public long LastIndexOf(byte[] pattern, long endExclusive, int maxScan = 4096)
    {
        if (pattern is null || pattern.Length == 0) throw new ArgumentException("Pattern must be non-empty.", nameof(pattern));

        long len = Length;
        if (endExclusive < 0 || endExclusive > len) endExclusive = len;

        long from = Math.Max(0, endExclusive - maxScan);
        int count = (int)(endExclusive - from);
        if (count < pattern.Length) return -1;

        byte[] window = ReadBytes(from, count);
        for (int i = window.Length - pattern.Length; i >= 0; i--)
        {
            if (Matches(window, i, pattern)) return from + i;
        }
        return -1;
    }

    private static bool Matches(byte[] haystack, int offset, byte[] needle)
    {
        for (int i = 0; i < needle.Length; i++)
        {
            if (haystack[offset + i] != needle[i]) return false;
        }
        return true;
    }

    /// <summary>Releases the underlying stream if this source owns it.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsStream) _stream.Dispose();
    }
}
