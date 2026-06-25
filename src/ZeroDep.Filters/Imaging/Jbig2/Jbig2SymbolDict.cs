using System.Collections.Generic;

namespace ZeroDep.Filters.Jbig2;

/// <summary>
/// Decodes a JBIG2 symbol dictionary (ITU-T T.88 §6.5), arithmetic variant (SDHUFF = 0, SDREFAGG = 0 —
/// the forms that appear in real PDFs). Produces the list of exported symbol bitmaps, which text
/// regions then place. Huffman / refinement-aggregate dictionaries are reported as unsupported.
/// </summary>
internal static class Jbig2SymbolDict
{
    public static bool TrySupported(byte[] d, int dataStart)
    {
        int flags = (d[dataStart] << 8) | d[dataStart + 1];
        bool sdhuff = (flags & 1) != 0;
        bool sdrefagg = (flags & 2) != 0;
        return !sdhuff && !sdrefagg;
    }

    /// <summary>Decodes the dictionary; returns its exported symbols (input symbols come from referred dicts).</summary>
    public static List<Jbig2Bitmap> Decode(byte[] d, int dataStart, int dataLen, IReadOnlyList<Jbig2Bitmap> inputSymbols)
    {
        int flags = (d[dataStart] << 8) | d[dataStart + 1];
        bool sdhuff = (flags & 1) != 0;
        bool sdrefagg = (flags & 2) != 0;
        int sdTemplate = (flags >> 10) & 3;

        var exported = new List<Jbig2Bitmap>();
        if (sdhuff || sdrefagg)
        {
            return exported; // unsupported variant
        }

        int p = dataStart + 2;
        int nat = sdTemplate == 0 ? 4 : 1;
        var at = new (int X, int Y)[nat];
        for (int i = 0; i < nat; i++)
        {
            at[i] = ((sbyte)d[p], (sbyte)d[p + 1]);
            p += 2;
        }

        int numExSyms = (int)U32(d, p);
        p += 4;
        int numNewSyms = (int)U32(d, p);
        p += 4;

        var mq = new MqDecoder(d, p, dataStart + dataLen);
        var iadh = new ArithContext(512);
        var iadw = new ArithContext(512);
        var iaex = new ArithContext(512);
        var gb = new ArithContext(1 << 16);

        var newSymbols = new List<Jbig2Bitmap>();
        int hcHeight = 0;

        while (newSymbols.Count < numNewSyms)
        {
            int hcdh = Jbig2ArithInt.DecodeInt(mq, iadh);
            if (hcdh == Jbig2ArithInt.Oob)
            {
                break;
            }

            hcHeight += hcdh;
            if (hcHeight <= 0 || hcHeight > 1 << 15)
            {
                break;
            }

            int symWidth = 0;
            while (true)
            {
                int dw = Jbig2ArithInt.DecodeInt(mq, iadw);
                if (dw == Jbig2ArithInt.Oob)
                {
                    break; // end of height class
                }

                symWidth += dw;
                if (symWidth <= 0 || symWidth > 1 << 15 || newSymbols.Count >= numNewSyms)
                {
                    break;
                }

                newSymbols.Add(Jbig2GenericRegion.Decode(mq, gb, symWidth, hcHeight, sdTemplate, at, false));
            }
        }

        // Build the export list: IAEX run-lengths over (input + new), alternating skip/export.
        var all = new List<Jbig2Bitmap>(inputSymbols.Count + newSymbols.Count);
        all.AddRange(inputSymbols);
        all.AddRange(newSymbols);

        int index = 0;
        bool exportFlag = false;
        int guard = 0;
        while (index < all.Count && exported.Count < numExSyms && guard++ < all.Count + 4)
        {
            int run = Jbig2ArithInt.DecodeInt(mq, iaex);
            if (run == Jbig2ArithInt.Oob || run < 0)
            {
                break;
            }

            if (exportFlag)
            {
                for (int i = 0; i < run && index < all.Count; i++, index++)
                {
                    exported.Add(all[index]);
                }
            }
            else
            {
                index += run;
            }

            exportFlag = !exportFlag;
        }

        // Fallback: if export flags were unusable, export the new symbols directly.
        if (exported.Count == 0 && newSymbols.Count > 0)
        {
            exported.AddRange(newSymbols);
        }

        return exported;
    }

    private static long U32(byte[] d, int o)
        => ((long)d[o] << 24) | ((long)d[o + 1] << 16) | ((long)d[o + 2] << 8) | d[o + 3];
}
