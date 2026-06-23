using System;
using System.Collections.Generic;

namespace ZeroDep.Filters;

/// <summary>Decodes PDF <c>/RunLengthDecode</c> data (ISO 32000-2 §7.4.5).</summary>
public static class RunLengthDecode
{
    /// <summary>Decodes run-length data; 128 is the end-of-data marker.</summary>
    public static byte[] Decode(byte[] input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        var output = new List<byte>(input.Length * 2);
        int i = 0;
        while (i < input.Length)
        {
            int length = input[i++];
            if (length == 128) break; // EOD
            if (length < 128)
            {
                for (int j = 0; j <= length && i < input.Length; j++) output.Add(input[i++]);
            }
            else if (i < input.Length)
            {
                byte value = input[i++];
                int repeat = 257 - length;
                for (int j = 0; j < repeat; j++) output.Add(value);
            }
        }
        return output.ToArray();
    }
}
