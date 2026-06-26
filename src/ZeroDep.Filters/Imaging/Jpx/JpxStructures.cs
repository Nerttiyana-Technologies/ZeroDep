using System.Collections.Generic;

namespace ZeroDep.Filters.Jpx;

/// <summary>One coded block: its clipped sub-band rectangle, packet-header state, and the byte ranges of
/// its compressed passes gathered across layers (ITU-T T.800 §B.10).</summary>
internal sealed class JpxCodeBlock
{
    public int X0;   // clipped sub-band coordinates
    public int Y0;
    public int X1;
    public int Y1;

    public int GridX; // position in the sub-band's code-block grid
    public int GridY;

    public int LocalX; // position within the owning precinct's code-block grid (tag-tree leaf)
    public int LocalY;

    public bool Included;
    public int LBlock = 3;
    public int ZeroBitPlanes;
    public int NumPasses;

    // Per-packet scratch (filled while reading a packet header).
    public int PendingPasses;
    public int PendingLength;

    /// <summary>(offset, length) byte ranges (in the tile bitstream) of this block's data, in layer order.</summary>
    public readonly List<(int Offset, int Length)> Segments = new List<(int, int)>();

    public int Width => X1 - X0;

    public int Height => Y1 - Y0;
}

/// <summary>A sub-band of a resolution level: orientation, geometry, quantization, and its code-blocks
/// grouped by precinct with the two tag trees used for packet headers.</summary>
internal sealed class JpxSubband
{
    public int Orientation; // 0=LL, 1=HL, 2=LH, 3=HH

    public int X0;
    public int Y0;
    public int X1;
    public int Y1;

    public int Exponent;
    public int Mantissa;
    public int NumBps;       // Mb = guard bits + exponent - 1

    public JpxCodeBlock[] CodeBlocks = System.Array.Empty<JpxCodeBlock>();

    /// <summary>Code-block indices (into <see cref="CodeBlocks"/>) per precinct, raster order.</summary>
    public List<int>[] PrecinctCodeBlocks = System.Array.Empty<List<int>>();

    public JpxTagTree[] InclusionTrees = System.Array.Empty<JpxTagTree>();

    public JpxTagTree[] ZeroBitTrees = System.Array.Empty<JpxTagTree>();

    public int Width => X1 - X0;

    public int Height => Y1 - Y0;
}

/// <summary>A resolution level of a tile-component: its rectangle, precinct grid, and sub-bands.</summary>
internal sealed class JpxResolution
{
    public int ResLevel;

    public int X0;
    public int Y0;
    public int X1;
    public int Y1;

    public int NumPrecinctsWide;
    public int NumPrecinctsHigh;

    public JpxSubband[] Subbands = System.Array.Empty<JpxSubband>();

    public int Width => X1 - X0;

    public int Height => Y1 - Y0;
}

/// <summary>A tile-component: its sample rectangle, coding/quantization parameters, and resolution levels.</summary>
internal sealed class JpxTileComponent
{
    public int Component;

    public int X0;
    public int Y0;
    public int X1;
    public int Y1;

    public JpxCod Cod = new JpxCod();
    public JpxQcd Qcd = new JpxQcd();
    public JpxComponent Siz = new JpxComponent();

    public JpxResolution[] Resolutions = System.Array.Empty<JpxResolution>();

    public int Width => X1 - X0;

    public int Height => Y1 - Y0;
}
