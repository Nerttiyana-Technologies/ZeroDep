using System;
using System.Collections.Generic;

namespace ZeroDep.Filters;

/// <summary>The coding process declared by a JPEG frame header (SOFn marker), per ITU-T T.81.</summary>
public enum JpegMode
{
    /// <summary>Baseline sequential DCT (SOF0) — the common case.</summary>
    Baseline = 0,

    /// <summary>Extended sequential DCT (SOF1).</summary>
    ExtendedSequential,

    /// <summary>Progressive DCT (SOF2).</summary>
    Progressive,

    /// <summary>A process ZeroDep does not decode (e.g. lossless or arithmetic-coded).</summary>
    Unsupported,
}

/// <summary>One color component declared in a JPEG frame header.</summary>
public sealed class JpegComponent
{
    /// <summary>The component identifier.</summary>
    public int Id { get; init; }

    /// <summary>The horizontal sampling factor (1–4).</summary>
    public int HorizontalSampling { get; init; }

    /// <summary>The vertical sampling factor (1–4).</summary>
    public int VerticalSampling { get; init; }

    /// <summary>The id of the quantization table this component uses.</summary>
    public int QuantizationTableId { get; init; }
}

/// <summary>
/// Structural metadata parsed from a JPEG (<c>/DCTDecode</c>) stream's headers: dimensions, coding
/// process, components and sampling, plus the quantization and Huffman tables the pixel decoder needs.
/// Parsing stops at the start of scan; no entropy-coded pixel data is decoded here.
/// </summary>
public sealed class JpegMetadata
{
    /// <summary>Image width in pixels.</summary>
    public int Width { get; init; }

    /// <summary>Image height in pixels.</summary>
    public int Height { get; init; }

    /// <summary>Sample precision in bits (typically 8).</summary>
    public int Precision { get; init; }

    /// <summary>The coding process.</summary>
    public JpegMode Mode { get; init; }

    /// <summary>The declared color components, in frame order.</summary>
    public IReadOnlyList<JpegComponent> Components { get; init; } = Array.Empty<JpegComponent>();

    /// <summary>The number of components.</summary>
    public int ComponentCount => Components.Count;

    /// <summary>The MCU restart interval (0 when no DRI marker is present).</summary>
    public int RestartInterval { get; init; }

    /// <summary>
    /// The Adobe APP14 colour transform: 0 = none (CMYK or RGB), 1 = YCbCr, 2 = YCCK; or -1 when no
    /// Adobe marker is present. Determines 4-component colour handling and the inverted-CMYK convention.
    /// </summary>
    public int AdobeTransform { get; init; } = -1;

    /// <summary>Quantization tables by id, each 64 entries in zig-zag order. (Used by the pixel decoder.)</summary>
    internal IReadOnlyDictionary<int, int[]> QuantizationTables { get; init; } = new Dictionary<int, int[]>();

    /// <summary>Huffman tables parsed from DHT markers. (Used by the pixel decoder.)</summary>
    internal IReadOnlyList<JpegHuffmanTable> HuffmanTables { get; init; } = Array.Empty<JpegHuffmanTable>();
}

/// <summary>A Huffman table parsed from a DHT marker, retained for the entropy-decode stage.</summary>
internal sealed class JpegHuffmanTable
{
    /// <summary>Table class: 0 = DC, 1 = AC.</summary>
    public int TableClass { get; init; }

    /// <summary>Table identifier (0–3).</summary>
    public int Id { get; init; }

    /// <summary>The number of codes of each length 1..16.</summary>
    public byte[] CodeLengthCounts { get; init; } = new byte[16];

    /// <summary>The symbol values, in canonical code order.</summary>
    public byte[] Symbols { get; init; } = Array.Empty<byte>();
}
