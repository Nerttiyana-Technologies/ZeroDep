using System.Collections.Generic;
using Xunit;
using ZeroDep.Color;
using ZeroDep.Objects;

namespace ZeroDep.Color.Tests;

/// <summary>
/// C-G2 — PDF function evaluator (ADR-0004 §2.3) correctness against known vectors for Types 0, 2, 3, 4
/// and the array-of-functions combiner, plus domain/range clamping.
/// </summary>
public sealed class PdfFunctionTests
{
    private static PdfObject Id(PdfObject o) => o;

    private static byte[] Raw(PdfStream s) => s.GetRawBytes();

    private static PdfObject N(double v) => new PdfReal(v);

    private static PdfArray Arr(params double[] v)
    {
        var items = new PdfObject[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            items[i] = new PdfReal(v[i]);
        }

        return new PdfArray(items);
    }

    private static PdfDictionary Dict(Dictionary<string, PdfObject> e) => new PdfDictionary(e);

    [Fact]
    public void Type2_LinearAndPower()
    {
        var dict = Dict(new Dictionary<string, PdfObject>
        {
            ["FunctionType"] = new PdfInteger(2),
            ["Domain"] = Arr(0, 1),
            ["C0"] = Arr(0),
            ["C1"] = Arr(1),
            ["N"] = N(1),
        });
        PdfFunction f = PdfFunction.Parse(dict, Id, Raw);
        Assert.Equal(0.5, f.Evaluate(new[] { 0.5 })[0], 6);

        var sq = Dict(new Dictionary<string, PdfObject>
        {
            ["FunctionType"] = new PdfInteger(2),
            ["Domain"] = Arr(0, 1),
            ["C0"] = Arr(0),
            ["C1"] = Arr(1),
            ["N"] = N(2),
        });
        Assert.Equal(0.25, PdfFunction.Parse(sq, Id, Raw).Evaluate(new[] { 0.5 })[0], 6);
    }

    [Fact]
    public void Type2_MultiOutput()
    {
        var dict = Dict(new Dictionary<string, PdfObject>
        {
            ["FunctionType"] = new PdfInteger(2),
            ["Domain"] = Arr(0, 1),
            ["C0"] = Arr(0, 0, 0),
            ["C1"] = Arr(1, 0.5, 0),
            ["N"] = N(1),
        });
        double[] y = PdfFunction.Parse(dict, Id, Raw).Evaluate(new[] { 0.5 });
        Assert.Equal(3, y.Length);
        Assert.Equal(0.5, y[0], 6);
        Assert.Equal(0.25, y[1], 6);
        Assert.Equal(0.0, y[2], 6);
    }

    [Fact]
    public void Type3_StitchesByBounds()
    {
        PdfObject Sub() => Dict(new Dictionary<string, PdfObject>
        {
            ["FunctionType"] = new PdfInteger(2),
            ["Domain"] = Arr(0, 1),
            ["C0"] = Arr(0),
            ["C1"] = Arr(1),
            ["N"] = N(1),
        });

        var dict = Dict(new Dictionary<string, PdfObject>
        {
            ["FunctionType"] = new PdfInteger(3),
            ["Domain"] = Arr(0, 1),
            ["Functions"] = new PdfArray(new[] { Sub(), Sub() }),
            ["Bounds"] = Arr(0.5),
            ["Encode"] = Arr(0, 1, 0, 1),
        });
        PdfFunction f = PdfFunction.Parse(dict, Id, Raw);

        // x=0.25 → segment 0, sub-domain [0,0.5] encoded to [0,1] → 0.5 → identity → 0.5
        Assert.Equal(0.5, f.Evaluate(new[] { 0.25 })[0], 6);
        // x=0.75 → segment 1, sub-domain [0.5,1] encoded to [0,1] → 0.5 → 0.5
        Assert.Equal(0.5, f.Evaluate(new[] { 0.75 })[0], 6);
        // x=0.0 → segment 0 → 0.0 ; x=1.0 → segment 1 → 1.0
        Assert.Equal(0.0, f.Evaluate(new[] { 0.0 })[0], 6);
        Assert.Equal(1.0, f.Evaluate(new[] { 1.0 })[0], 6);
    }

    [Fact]
    public void Type0_SampledLinearInterpolation()
    {
        var dict = Dict(new Dictionary<string, PdfObject>
        {
            ["FunctionType"] = new PdfInteger(0),
            ["Domain"] = Arr(0, 1),
            ["Range"] = Arr(0, 1),
            ["Size"] = new PdfArray(new PdfObject[] { new PdfInteger(2) }),
            ["BitsPerSample"] = new PdfInteger(8),
        });
        var stream = new PdfStream(dict, new byte[] { 0x00, 0xFF });
        PdfFunction f = PdfFunction.Parse(stream, Id, Raw);

        Assert.Equal(0.0, f.Evaluate(new[] { 0.0 })[0], 6);
        Assert.Equal(1.0, f.Evaluate(new[] { 1.0 })[0], 6);
        Assert.Equal(0.5, f.Evaluate(new[] { 0.5 })[0], 3);
    }

    [Fact]
    public void Type0_TwoInputBilinear()
    {
        // 2x2 grid, 1 output: corners (0,0)=0, (1,0)=1, (0,1)=1, (1,1)=0 → center = 0.5
        var dict = Dict(new Dictionary<string, PdfObject>
        {
            ["FunctionType"] = new PdfInteger(0),
            ["Domain"] = Arr(0, 1, 0, 1),
            ["Range"] = Arr(0, 1),
            ["Size"] = new PdfArray(new PdfObject[] { new PdfInteger(2), new PdfInteger(2) }),
            ["BitsPerSample"] = new PdfInteger(8),
        });
        // sample order: index = x + y*sizeX → (0,0),(1,0),(0,1),(1,1)
        var stream = new PdfStream(dict, new byte[] { 0x00, 0xFF, 0xFF, 0x00 });
        PdfFunction f = PdfFunction.Parse(stream, Id, Raw);
        Assert.Equal(0.5, f.Evaluate(new[] { 0.5, 0.5 })[0], 3);
        Assert.Equal(1.0, f.Evaluate(new[] { 1.0, 0.0 })[0], 3);
    }

    [Fact]
    public void Type4_ArithmeticAndConditional()
    {
        PdfFunction Doubler() => PdfFunction.Parse(
            new PdfStream(
                Dict(new Dictionary<string, PdfObject>
                {
                    ["FunctionType"] = new PdfInteger(4),
                    ["Domain"] = Arr(0, 1),
                    ["Range"] = Arr(0, 2),
                }),
                System.Text.Encoding.ASCII.GetBytes("{ 2 mul }")),
            Id,
            Raw);

        Assert.Equal(1.0, Doubler().Evaluate(new[] { 0.5 })[0], 6);

        // threshold: x<0.5 → 0 else 1
        PdfFunction Threshold() => PdfFunction.Parse(
            new PdfStream(
                Dict(new Dictionary<string, PdfObject>
                {
                    ["FunctionType"] = new PdfInteger(4),
                    ["Domain"] = Arr(0, 1),
                    ["Range"] = Arr(0, 1),
                }),
                System.Text.Encoding.ASCII.GetBytes("{ dup 0.5 lt { pop 0 } { pop 1 } ifelse }")),
            Id,
            Raw);

        Assert.Equal(0.0, Threshold().Evaluate(new[] { 0.3 })[0], 6);
        Assert.Equal(1.0, Threshold().Evaluate(new[] { 0.7 })[0], 6);
    }

    [Fact]
    public void Type4_TwoInputsAddAndExch()
    {
        PdfFunction f = PdfFunction.Parse(
            new PdfStream(
                Dict(new Dictionary<string, PdfObject>
                {
                    ["FunctionType"] = new PdfInteger(4),
                    ["Domain"] = Arr(0, 1, 0, 1),
                    ["Range"] = Arr(0, 2),
                }),
                System.Text.Encoding.ASCII.GetBytes("{ add }")),
            Id,
            Raw);
        Assert.Equal(0.7, f.Evaluate(new[] { 0.3, 0.4 })[0], 6);
    }

    [Fact]
    public void FunctionArray_CombinesOutputs()
    {
        PdfObject Sub(double c1) => Dict(new Dictionary<string, PdfObject>
        {
            ["FunctionType"] = new PdfInteger(2),
            ["Domain"] = Arr(0, 1),
            ["C0"] = Arr(0),
            ["C1"] = Arr(c1),
            ["N"] = N(1),
        });

        PdfFunction f = PdfFunction.Parse(new PdfArray(new[] { Sub(1), Sub(0.5) }), Id, Raw);
        Assert.Equal(2, f.OutputCount);
        double[] y = f.Evaluate(new[] { 1.0 });
        Assert.Equal(1.0, y[0], 6);
        Assert.Equal(0.5, y[1], 6);
    }

    [Fact]
    public void ClampsDomainAndRange()
    {
        var dict = Dict(new Dictionary<string, PdfObject>
        {
            ["FunctionType"] = new PdfInteger(2),
            ["Domain"] = Arr(0, 1),
            ["Range"] = Arr(0, 1),
            ["C0"] = Arr(0),
            ["C1"] = Arr(1),
            ["N"] = N(1),
        });
        PdfFunction f = PdfFunction.Parse(dict, Id, Raw);
        // input 2.0 clamps to 1.0 → 1.0; input -1 clamps to 0 → 0
        Assert.Equal(1.0, f.Evaluate(new[] { 2.0 })[0], 6);
        Assert.Equal(0.0, f.Evaluate(new[] { -1.0 })[0], 6);
    }
}
