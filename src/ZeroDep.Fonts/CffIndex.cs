namespace ZeroDep.Fonts;

/// <summary>A CFF INDEX: a count-prefixed array of variable-length objects (Adobe TN #5176 §5).</summary>
internal sealed class CffIndex
{
    private readonly int[] _offsets; // Count+1 absolute offsets into the font data

    private CffIndex(int count, int[] offsets)
    {
        Count = count;
        _offsets = offsets;
    }

    /// <summary>An empty INDEX.</summary>
    public static CffIndex Empty { get; } = new CffIndex(0, new[] { 0 });

    /// <summary>The number of objects.</summary>
    public int Count { get; }

    /// <summary>The absolute (start, length) byte range of object <paramref name="i"/>.</summary>
    public (int Start, int Length) Item(int i) => (_offsets[i], _offsets[i + 1] - _offsets[i]);

    /// <summary>Reads an INDEX at <paramref name="pos"/>; returns the position just past it.</summary>
    public static int Read(byte[] data, int pos, out CffIndex index)
    {
        if (pos < 0 || pos + 2 > data.Length)
        {
            index = Empty;
            return pos;
        }

        int count = (data[pos] << 8) | data[pos + 1];
        if (count == 0)
        {
            index = Empty;
            return pos + 2;
        }

        int offSize = data[pos + 2];
        int offsetArray = pos + 3;
        int dataBase = offsetArray + ((count + 1) * offSize) - 1;

        var offsets = new int[count + 1];
        for (int i = 0; i <= count; i++)
        {
            int o = 0;
            int at = offsetArray + (i * offSize);
            for (int k = 0; k < offSize; k++)
            {
                o = (o << 8) | (at + k < data.Length ? data[at + k] : 0);
            }

            offsets[i] = dataBase + o;
        }

        index = new CffIndex(count, offsets);
        return offsets[count];
    }
}
