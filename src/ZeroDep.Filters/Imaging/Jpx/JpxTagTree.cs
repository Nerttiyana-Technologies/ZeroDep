namespace ZeroDep.Filters.Jpx;

/// <summary>
/// A tag tree (ITU-T T.800 §B.10.2): the hierarchical coder used in packet headers for code-block
/// inclusion and for the count of leading all-zero bit-planes. Each leaf value is decoded incrementally
/// against a rising threshold across layers; node state (current lower bound and whether it is final)
/// persists between calls, so repeated <see cref="Decode"/> calls for the same leaf resume where the
/// previous left off.
/// </summary>
internal sealed class JpxTagTree
{
    private readonly int _width;
    private readonly int _height;
    private readonly int[] _levelWidth;
    private readonly int[] _levelOffset;
    private readonly int _levels;
    private readonly int[] _value;   // current lower-bound estimate per node
    private readonly bool[] _known;  // true once the node value is exact

    public JpxTagTree(int width, int height)
    {
        _width = width < 1 ? 1 : width;
        _height = height < 1 ? 1 : height;

        // Build level dimensions from the leaf level up to the 1x1 root.
        int w = _width;
        int h = _height;
        int levels = 1;
        while (w > 1 || h > 1)
        {
            w = (w + 1) >> 1;
            h = (h + 1) >> 1;
            levels++;
        }

        _levels = levels;
        _levelWidth = new int[levels];
        _levelOffset = new int[levels];

        w = _width;
        h = _height;
        int total = 0;
        for (int i = 0; i < levels; i++)
        {
            _levelWidth[i] = w;
            _levelOffset[i] = total;
            total += w * h;
            w = (w + 1) >> 1;
            h = (h + 1) >> 1;
        }

        _value = new int[total];
        _known = new bool[total];
    }

    /// <summary>
    /// Decodes the leaf at (<paramref name="leafX"/>, <paramref name="leafY"/>) against
    /// <paramref name="threshold"/>, reading bits from <paramref name="reader"/> as needed. Returns the
    /// node's current value. The leaf is resolved (its true value is returned) when the result is
    /// strictly less than the threshold; a result equal to the threshold means "not yet resolved at this
    /// threshold". Pass a large threshold to fully resolve (e.g. for the zero-bit-plane count).
    /// </summary>
    public int Decode(JpxBitReader reader, int leafX, int leafY, int threshold)
    {
        // Walk root -> leaf, carrying the lower bound established by ancestors.
        int lowerBound = 0;
        int x = leafX;
        int y = leafY;

        // Precompute the (x,y) at each level for this leaf.
        // Level 0 is the leaf level; the root is the last level.
        var xs = new int[_levels];
        var ys = new int[_levels];
        for (int i = 0; i < _levels; i++)
        {
            xs[i] = x;
            ys[i] = y;
            x >>= 1;
            y >>= 1;
        }

        for (int level = _levels - 1; level >= 0; level--)
        {
            int idx = _levelOffset[level] + (ys[level] * _levelWidth[level]) + xs[level];
            if (_value[idx] < lowerBound)
            {
                _value[idx] = lowerBound;
            }

            while (!_known[idx] && _value[idx] < threshold)
            {
                if (reader.ReadBit() == 1)
                {
                    _known[idx] = true;
                }
                else
                {
                    _value[idx]++;
                }
            }

            lowerBound = _value[idx];
        }

        return lowerBound;
    }
}
