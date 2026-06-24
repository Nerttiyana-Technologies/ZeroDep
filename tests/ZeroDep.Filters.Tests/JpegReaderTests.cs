using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using ZeroDep.Filters;

namespace ZeroDep.Filters.Tests;

/// <summary>
/// Stage 1 of the JPEG (/DCTDecode) decoder: the container/frame parser. These tests build minimal
/// JPEG headers by hand and assert dimensions, components, mode, and table parsing — without any
/// entropy-coded pixel data. (ITU-T T.81 marker structure.)
/// </summary>
public sealed class JpegReaderTests
{
    [Fact]
    public void ParsesBaselineFrameHeader()
    {
        var bytes = new List<byte>();
        bytes.AddRange(new byte[] { 0xFF, 0xD8 });                       // SOI
        // SOF0: len=17, precision=8, height=16, width=32, 3 components
        bytes.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x10, 0x00, 0x20, 0x03 });
        bytes.AddRange(new byte[] { 0x01, 0x22, 0x00 });                 // comp 1: H=2 V=2 q=0
        bytes.AddRange(new byte[] { 0x02, 0x11, 0x01 });                 // comp 2: H=1 V=1 q=1
        bytes.AddRange(new byte[] { 0x03, 0x11, 0x01 });                 // comp 3: H=1 V=1 q=1
        // SOS: len=12
        bytes.AddRange(new byte[] { 0xFF, 0xDA, 0x00, 0x0C, 0x03, 0x01, 0x00, 0x02, 0x11, 0x03, 0x11, 0x00, 0x3F, 0x00 });

        JpegMetadata meta = JpegReader.ReadMetadata(bytes.ToArray());

        Assert.Equal(32, meta.Width);
        Assert.Equal(16, meta.Height);
        Assert.Equal(8, meta.Precision);
        Assert.Equal(JpegMode.Baseline, meta.Mode);
        Assert.Equal(3, meta.ComponentCount);

        JpegComponent y = meta.Components[0];
        Assert.Equal(1, y.Id);
        Assert.Equal(2, y.HorizontalSampling);
        Assert.Equal(2, y.VerticalSampling);
        Assert.Equal(0, y.QuantizationTableId);
        Assert.Equal(1, meta.Components[1].HorizontalSampling);
    }

    [Fact]
    public void ParsesQuantAndHuffmanAndRestartInterval()
    {
        var bytes = new List<byte>();
        bytes.AddRange(new byte[] { 0xFF, 0xD8 });                       // SOI

        // DQT: len=67, pq/tq=0x00 (8-bit, id 0), 64 entries all 0x10
        bytes.AddRange(new byte[] { 0xFF, 0xDB, 0x00, 0x43, 0x00 });
        bytes.AddRange(Enumerable.Repeat((byte)0x10, 64));

        // DHT: len=20, tc/th=0x00 (DC, id 0), one code of length 2 -> symbol 0x05
        bytes.AddRange(new byte[] { 0xFF, 0xC4, 0x00, 0x14, 0x00 });
        bytes.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
        bytes.Add(0x05);

        // DRI: restart interval = 5
        bytes.AddRange(new byte[] { 0xFF, 0xDD, 0x00, 0x04, 0x00, 0x05 });

        // SOF0: 1 component, 8x8
        bytes.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x08, 0x00, 0x08, 0x01, 0x01, 0x11, 0x00 });

        // SOS
        bytes.AddRange(new byte[] { 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00 });

        JpegMetadata meta = JpegReader.ReadMetadata(bytes.ToArray());

        Assert.Equal(8, meta.Width);
        Assert.Equal(8, meta.Height);
        Assert.Equal(5, meta.RestartInterval);
        Assert.True(meta.QuantizationTables.ContainsKey(0));
        Assert.Equal(64, meta.QuantizationTables[0].Length);
        Assert.All(meta.QuantizationTables[0], v => Assert.Equal(16, v));
        JpegHuffmanTable table = Assert.Single(meta.HuffmanTables);
        Assert.Equal(0, table.TableClass);
        Assert.Equal(new byte[] { 0x05 }, table.Symbols);
    }

    [Fact]
    public void RejectsNonJpeg()
        => Assert.Throws<InvalidDataException>(() => JpegReader.ReadMetadata(new byte[] { 0x00, 0x01, 0x02 }));

    [Fact]
    public void RejectsJpegWithoutFrameHeader()
    {
        // SOI immediately followed by EOI — no SOFn.
        Assert.Throws<InvalidDataException>(() => JpegReader.ReadMetadata(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }));
    }
}
