using System;
using System.Collections.Generic;

namespace ZeroDep.Filters;

/// <summary>Decodes PDF <c>/ASCIIHexDecode</c> data (ISO 32000-2 §7.4.2).</summary>
public static class AsciiHexDecode
{
    /// <summary>Decodes hex-encoded data, ignoring whitespace, ending at '&gt;'.</summary>
    public static byte[] Decode(byte[] input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        var output = new List<byte>(input.Length / 2);
        int high = -1;
        foreach (byte b in input)
        {
            if (b == (byte)'>') break;
            int v = HexValue(b);
            if (v < 0) continue;
            if (high < 0) high = v;
            else { output.Add((byte)((high << 4) | v)); high = -1; }
        }
        if (high >= 0) output.Add((byte)(high << 4)); // odd trailing nibble padded with 0
        return output.ToArray();
    }

    private static int HexValue(int b)
    {
        if (b >= '0' && b <= '9') return b - '0';
        if (b >= 'A' && b <= 'F') return b - 'A' + 10;
        if (b >= 'a' && b <= 'f') return b - 'a' + 10;
        return -1;
    }
}
