namespace ZeroDep.Analysis;

/// <summary>Per-page structural signals gathered during the text content pass (ADR-0003 P1).</summary>
internal readonly struct PageStructure
{
    public PageStructure(int pageIndex, int rulingLineCount, int fontDistinctCount)
    {
        PageIndex = pageIndex;
        RulingLineCount = rulingLineCount;
        FontDistinctCount = fontDistinctCount;
    }

    public int PageIndex { get; }

    public int RulingLineCount { get; }

    public int FontDistinctCount { get; }
}
