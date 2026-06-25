namespace ZeroDep.Filters.Jbig2;

/// <summary>
/// JBIG2 arithmetic integer decoding (ITU-T T.88 Annex A): the <c>IAx</c> integer procedures and the
/// <c>IAID</c> symbol-ID procedure, used by symbol dictionaries and text regions.
/// </summary>
internal static class Jbig2ArithInt
{
    /// <summary>Sentinel returned for an out-of-band (OOB) integer.</summary>
    public const int Oob = int.MinValue;

    /// <summary>Decodes one integer using an IAx context (returns <see cref="Oob"/> for out-of-band).</summary>
    public static int DecodeInt(MqDecoder mq, ArithContext cx)
    {
        int prev = 1;

        int ReadBits(int count)
        {
            int v = 0;
            for (int i = 0; i < count; i++)
            {
                int bit = mq.Decode(cx, prev);
                prev = prev < 256 ? (prev << 1) | bit : ((((prev << 1) | bit) & 511) | 256);
                v = (v << 1) | bit;
            }

            return v;
        }

        int sign = ReadBits(1);
        int value;
        if (ReadBits(1) == 0)
        {
            value = ReadBits(2);
        }
        else if (ReadBits(1) == 0)
        {
            value = ReadBits(4) + 4;
        }
        else if (ReadBits(1) == 0)
        {
            value = ReadBits(6) + 20;
        }
        else if (ReadBits(1) == 0)
        {
            value = ReadBits(8) + 84;
        }
        else if (ReadBits(1) == 0)
        {
            value = ReadBits(12) + 340;
        }
        else
        {
            value = ReadBits(32) + 4436;
        }

        if (sign == 0)
        {
            return value;
        }

        return value > 0 ? -value : Oob;
    }

    /// <summary>Decodes a symbol ID of the given code length using the IAID context.</summary>
    public static int DecodeIaid(MqDecoder mq, ArithContext cx, int codeLength)
    {
        int prev = 1;
        for (int i = 0; i < codeLength; i++)
        {
            int bit = mq.Decode(cx, prev);
            prev = (prev << 1) | bit;
        }

        return prev - (1 << codeLength);
    }
}
