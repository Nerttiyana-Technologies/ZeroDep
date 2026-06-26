namespace ZeroDep.Fonts;

/// <summary>
/// A parsed <c>cmap</c> (character-to-glyph) table — the best Unicode subtable, format 4 (BMP segment
/// mapping) or format 12 (full Unicode groups). Used to map code points to glyph ids for standalone use
/// and tests; the PDF font dict's own encoding drives the live text path.
/// </summary>
internal sealed class CmapTable
{
    private readonly byte[] _data;
    private readonly int _format;
    private readonly int _subtable;

    // format 4
    private readonly int _segCount;
    private readonly int _endCodeOff;
    private readonly int _startCodeOff;
    private readonly int _idDeltaOff;
    private readonly int _idRangeOffOff;

    // format 12
    private readonly int _groupsOff;
    private readonly int _groupCount;

    private CmapTable(byte[] data, int format, int subtable, int segCount, int endCodeOff, int startCodeOff, int idDeltaOff, int idRangeOffOff, int groupsOff, int groupCount)
    {
        _data = data;
        _format = format;
        _subtable = subtable;
        _segCount = segCount;
        _endCodeOff = endCodeOff;
        _startCodeOff = startCodeOff;
        _idDeltaOff = idDeltaOff;
        _idRangeOffOff = idRangeOffOff;
        _groupsOff = groupsOff;
        _groupCount = groupCount;
    }

    public static CmapTable? Parse(byte[] data, int cmapOffset)
    {
        var r = new BigEndianReader(data) { Position = cmapOffset + 2 };
        int numTables = r.ReadU16();

        int best = -1;
        int bestScore = -1;
        for (int i = 0; i < numTables; i++)
        {
            int platform = r.ReadU16();
            int encoding = r.ReadU16();
            int offset = (int)r.ReadU32();
            int score = (platform, encoding) switch
            {
                (3, 10) => 5, // Windows full Unicode
                (0, 6) => 5,
                (0, 4) => 5,
                (3, 1) => 4,  // Windows BMP
                (0, 3) => 4,
                (0, _) => 3,
                (3, 0) => 1,  // symbol
                _ => 0,
            };
            if (score > bestScore)
            {
                bestScore = score;
                best = cmapOffset + offset;
            }
        }

        if (best < 0)
        {
            return null;
        }

        var sr = new BigEndianReader(data) { Position = best };
        int format = sr.ReadU16();

        if (format == 4)
        {
            sr.Position = best + 6;
            int segCountX2 = sr.ReadU16();
            int segCount = segCountX2 / 2;
            int endCodeOff = best + 14;
            int startCodeOff = endCodeOff + (segCount * 2) + 2; // + reservedPad
            int idDeltaOff = startCodeOff + (segCount * 2);
            int idRangeOffOff = idDeltaOff + (segCount * 2);
            return new CmapTable(data, 4, best, segCount, endCodeOff, startCodeOff, idDeltaOff, idRangeOffOff, 0, 0);
        }

        if (format == 12)
        {
            sr.Position = best + 12;
            int nGroups = (int)sr.ReadU32();
            return new CmapTable(data, 12, best, 0, 0, 0, 0, 0, best + 16, nGroups);
        }

        return new CmapTable(data, format, best, 0, 0, 0, 0, 0, 0, 0);
    }

    public int Lookup(int codepoint)
        => _format switch
        {
            4 => LookupFormat4(codepoint),
            12 => LookupFormat12(codepoint),
            _ => 0,
        };

    private int LookupFormat4(int cp)
    {
        if (cp < 0 || cp > 0xFFFF)
        {
            return 0;
        }

        var r = new BigEndianReader(_data);
        for (int i = 0; i < _segCount; i++)
        {
            r.Position = _endCodeOff + (i * 2);
            int endCode = r.ReadU16();
            if (endCode < cp)
            {
                continue;
            }

            r.Position = _startCodeOff + (i * 2);
            int startCode = r.ReadU16();
            if (startCode > cp)
            {
                return 0;
            }

            r.Position = _idDeltaOff + (i * 2);
            int idDelta = r.ReadS16();
            r.Position = _idRangeOffOff + (i * 2);
            int idRangeOffset = r.ReadU16();

            if (idRangeOffset == 0)
            {
                return (cp + idDelta) & 0xFFFF;
            }

            int glyphAddr = _idRangeOffOff + (i * 2) + idRangeOffset + ((cp - startCode) * 2);
            r.Position = glyphAddr;
            int g = r.ReadU16();
            return g == 0 ? 0 : (g + idDelta) & 0xFFFF;
        }

        return 0;
    }

    private int LookupFormat12(int cp)
    {
        var r = new BigEndianReader(_data);
        int lo = 0;
        int hi = _groupCount - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            r.Position = _groupsOff + (mid * 12);
            long startChar = r.ReadU32();
            long endChar = r.ReadU32();
            long startGid = r.ReadU32();
            if (cp < startChar)
            {
                hi = mid - 1;
            }
            else if (cp > endChar)
            {
                lo = mid + 1;
            }
            else
            {
                return (int)(startGid + (cp - startChar));
            }
        }

        return 0;
    }
}
