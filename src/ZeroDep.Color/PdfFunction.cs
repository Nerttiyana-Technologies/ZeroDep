using System;
using System.Collections.Generic;
using ZeroDep.Objects;

namespace ZeroDep.Color;

/// <summary>
/// A PDF function (ISO 32000-2 §7.10): a mapping from <see cref="InputCount"/> inputs to
/// <see cref="OutputCount"/> outputs. Supports the four sampled/analytic types used by tint transforms,
/// shadings, and transfer functions: Type 0 (sampled), Type 2 (exponential interpolation), Type 3
/// (stitching), and Type 4 (PostScript calculator). An array of one-output functions is also accepted and
/// combined. Pure-BCL and deterministic; no rendering.
/// </summary>
public abstract class PdfFunction
{
    /// <summary>Creates a function with the given domain and (optional) range bounds.</summary>
    /// <param name="domain">Flat [min0,max0, min1,max1, …] over the inputs.</param>
    /// <param name="range">Flat [min0,max0, …] over the outputs, or null when the type does not require it.</param>
    protected PdfFunction(double[] domain, double[]? range)
    {
        Domain = domain ?? throw new ArgumentNullException(nameof(domain));
        Range = range;
    }

    /// <summary>Input bounds, flat: [min0,max0, min1,max1, …].</summary>
    public double[] Domain { get; }

    /// <summary>Output bounds, flat: [min0,max0, …], or null.</summary>
    public double[]? Range { get; }

    /// <summary>Number of inputs.</summary>
    public int InputCount => Domain.Length / 2;

    /// <summary>Number of outputs.</summary>
    public abstract int OutputCount { get; }

    /// <summary>
    /// Evaluates the function: clamps inputs to <see cref="Domain"/>, evaluates, and clamps outputs to
    /// <see cref="Range"/> when present.
    /// </summary>
    /// <param name="input">The input values (length should equal <see cref="InputCount"/>).</param>
    public double[] Evaluate(double[] input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        int m = InputCount;
        var x = new double[m];
        for (int i = 0; i < m; i++)
        {
            double v = i < input.Length ? input[i] : 0.0;
            x[i] = Clamp(v, Domain[2 * i], Domain[(2 * i) + 1]);
        }

        double[] y = EvaluateCore(x);

        if (Range is { } r)
        {
            int n = Math.Min(y.Length, r.Length / 2);
            for (int j = 0; j < n; j++)
            {
                y[j] = Clamp(y[j], r[2 * j], r[(2 * j) + 1]);
            }
        }

        return y;
    }

    /// <summary>Evaluates the function on inputs already clamped to <see cref="Domain"/>.</summary>
    /// <param name="input">The domain-clamped inputs.</param>
    protected abstract double[] EvaluateCore(double[] input);

    /// <summary>
    /// Parses a PDF function object (dictionary, stream, or an array of one-output functions).
    /// </summary>
    /// <param name="obj">The function object (may be an indirect reference).</param>
    /// <param name="resolve">Resolves indirect references to direct objects.</param>
    /// <param name="decodeStream">Decodes a stream's filters to its raw bytes (for Type 0 and Type 4).</param>
    public static PdfFunction Parse(PdfObject obj, Func<PdfObject, PdfObject> resolve, Func<PdfStream, byte[]> decodeStream)
    {
        if (obj is null)
        {
            throw new ArgumentNullException(nameof(obj));
        }

        if (resolve is null)
        {
            throw new ArgumentNullException(nameof(resolve));
        }

        if (decodeStream is null)
        {
            throw new ArgumentNullException(nameof(decodeStream));
        }

        PdfObject ro = resolve(obj);

        if (ro is PdfArray array)
        {
            var parts = new List<PdfFunction>(array.Count);
            foreach (PdfObject item in array.Items)
            {
                parts.Add(Parse(item, resolve, decodeStream));
            }

            return new FunctionArray(parts.ToArray());
        }

        PdfDictionary dict;
        byte[]? streamData = null;
        if (ro is PdfStream stream)
        {
            dict = stream.Dictionary;
            streamData = decodeStream(stream);
        }
        else if (ro is PdfDictionary d)
        {
            dict = d;
        }
        else
        {
            throw new NotSupportedException("Unsupported PDF function object.");
        }

        int type = (int)(Num(dict["FunctionType"], resolve) ?? -1);
        double[] domain = NumArray(dict["Domain"], resolve) ?? new double[] { 0.0, 1.0 };
        double[]? range = NumArray(dict["Range"], resolve);

        return type switch
        {
            0 => SampledFunction.Create(dict, streamData ?? Array.Empty<byte>(), domain, range, resolve),
            2 => ExponentialFunction.Create(dict, domain, range, resolve),
            3 => StitchingFunction.Create(dict, domain, range, resolve, decodeStream),
            4 => PostScriptFunction.Create(streamData ?? Array.Empty<byte>(), domain, range),
            _ => throw new NotSupportedException($"PDF function type {type} is not supported."),
        };
    }

    private protected static double Clamp(double v, double lo, double hi)
    {
        if (hi < lo)
        {
            (lo, hi) = (hi, lo);
        }

        return v < lo ? lo : (v > hi ? hi : v);
    }

    private protected static double Interpolate(double x, double xMin, double xMax, double yMin, double yMax)
    {
        if (xMax == xMin)
        {
            return yMin;
        }

        return yMin + ((x - xMin) * (yMax - yMin) / (xMax - xMin));
    }

    private protected static double? Num(PdfObject? o, Func<PdfObject, PdfObject> resolve)
        => resolve(o ?? PdfNull.Instance) is PdfNumber n ? n.AsDouble : null;

    private protected static double[]? NumArray(PdfObject? o, Func<PdfObject, PdfObject> resolve)
    {
        if (resolve(o ?? PdfNull.Instance) is not PdfArray a)
        {
            return null;
        }

        var result = new double[a.Count];
        for (int i = 0; i < a.Count; i++)
        {
            result[i] = resolve(a[i]) is PdfNumber n ? n.AsDouble : 0.0;
        }

        return result;
    }

    private protected static int[]? IntArray(PdfObject? o, Func<PdfObject, PdfObject> resolve)
    {
        double[]? d = NumArray(o, resolve);
        if (d is null)
        {
            return null;
        }

        var result = new int[d.Length];
        for (int i = 0; i < d.Length; i++)
        {
            result[i] = (int)Math.Round(d[i]);
        }

        return result;
    }
}

/// <summary>An array of one-output functions presented as a single multi-output function (§7.10.1).</summary>
internal sealed class FunctionArray : PdfFunction
{
    private readonly PdfFunction[] _parts;

    public FunctionArray(PdfFunction[] parts)
        : base(parts.Length > 0 ? parts[0].Domain : new double[] { 0.0, 1.0 }, null)
        => _parts = parts;

    public override int OutputCount
    {
        get
        {
            int n = 0;
            foreach (PdfFunction p in _parts)
            {
                n += p.OutputCount;
            }

            return n;
        }
    }

    protected override double[] EvaluateCore(double[] input)
    {
        var outv = new double[OutputCount];
        int k = 0;
        foreach (PdfFunction p in _parts)
        {
            double[] y = p.Evaluate(input);
            Array.Copy(y, 0, outv, k, y.Length);
            k += y.Length;
        }

        return outv;
    }
}

/// <summary>Type 2 — exponential interpolation: y_j = C0_j + x^N · (C1_j − C0_j) (§7.10.3).</summary>
internal sealed class ExponentialFunction : PdfFunction
{
    private readonly double[] _c0;
    private readonly double[] _c1;
    private readonly double _n;

    private ExponentialFunction(double[] domain, double[]? range, double[] c0, double[] c1, double n)
        : base(domain, range)
    {
        _c0 = c0;
        _c1 = c1;
        _n = n;
    }

    public override int OutputCount => _c0.Length;

    public static ExponentialFunction Create(PdfDictionary dict, double[] domain, double[]? range, Func<PdfObject, PdfObject> resolve)
    {
        double[] c0 = NumArray(dict["C0"], resolve) ?? new[] { 0.0 };
        double[] c1 = NumArray(dict["C1"], resolve) ?? new[] { 1.0 };
        double n = Num(dict["N"], resolve) ?? 1.0;
        return new ExponentialFunction(domain, range, c0, c1, n);
    }

    protected override double[] EvaluateCore(double[] input)
    {
        double x = input.Length > 0 ? input[0] : 0.0;
        double xn = _n == 1.0 ? x : Math.Pow(x, _n);
        var y = new double[_c0.Length];
        for (int j = 0; j < y.Length; j++)
        {
            double c1 = j < _c1.Length ? _c1[j] : 0.0;
            y[j] = _c0[j] + (xn * (c1 - _c0[j]));
        }

        return y;
    }
}

/// <summary>Type 3 — stitching: routes the single input to one of k sub-functions by bounds (§7.10.4).</summary>
internal sealed class StitchingFunction : PdfFunction
{
    private readonly PdfFunction[] _functions;
    private readonly double[] _bounds;
    private readonly double[] _encode;

    private StitchingFunction(double[] domain, double[]? range, PdfFunction[] functions, double[] bounds, double[] encode)
        : base(domain, range)
    {
        _functions = functions;
        _bounds = bounds;
        _encode = encode;
    }

    public override int OutputCount => _functions.Length > 0 ? _functions[0].OutputCount : 0;

    public static StitchingFunction Create(
        PdfDictionary dict, double[] domain, double[]? range, Func<PdfObject, PdfObject> resolve, Func<PdfStream, byte[]> decodeStream)
    {
        var functions = new List<PdfFunction>();
        if (resolve(dict["Functions"] ?? PdfNull.Instance) is PdfArray fns)
        {
            foreach (PdfObject f in fns.Items)
            {
                functions.Add(Parse(f, resolve, decodeStream));
            }
        }

        double[] bounds = NumArray(dict["Bounds"], resolve) ?? Array.Empty<double>();
        double[] encode = NumArray(dict["Encode"], resolve) ?? Array.Empty<double>();
        return new StitchingFunction(domain, range, functions.ToArray(), bounds, encode);
    }

    protected override double[] EvaluateCore(double[] input)
    {
        int k = _functions.Length;
        if (k == 0)
        {
            return new double[OutputCount];
        }

        double x = input.Length > 0 ? input[0] : 0.0;

        int i = 0;
        while (i < k - 1 && x >= _bounds[i])
        {
            i++;
        }

        double lo = i == 0 ? Domain[0] : _bounds[i - 1];
        double hi = i == k - 1 ? Domain[1] : _bounds[i];
        double eLo = (2 * i) < _encode.Length ? _encode[2 * i] : 0.0;
        double eHi = ((2 * i) + 1) < _encode.Length ? _encode[(2 * i) + 1] : 1.0;

        double e = Interpolate(x, lo, hi, eLo, eHi);
        return _functions[i].Evaluate(new[] { e });
    }
}

/// <summary>Type 0 — sampled function with multilinear interpolation over the sample grid (§7.10.2).</summary>
internal sealed class SampledFunction : PdfFunction
{
    private readonly int[] _size;
    private readonly int _bps;
    private readonly double[] _encode;
    private readonly double[] _decode;
    private readonly double[] _range;
    private readonly byte[] _samples;
    private readonly double _maxSample;

    private SampledFunction(double[] domain, double[] range, int[] size, int bps, double[] encode, double[] decode, byte[] samples)
        : base(domain, range)
    {
        _size = size;
        _bps = bps;
        _encode = encode;
        _decode = decode;
        _range = range;
        _samples = samples;
        _maxSample = bps >= 32 ? 4294967295.0 : (double)((1L << bps) - 1);
    }

    public override int OutputCount => _range.Length / 2;

    public static SampledFunction Create(PdfDictionary dict, byte[] data, double[] domain, double[]? range, Func<PdfObject, PdfObject> resolve)
    {
        int[] size = IntArray(dict["Size"], resolve) ?? Array.Empty<int>();
        int bps = (int)(Num(dict["BitsPerSample"], resolve) ?? 8);
        double[] rng = range ?? Array.Empty<double>();
        int m = domain.Length / 2;

        double[] encode = NumArray(dict["Encode"], resolve) ?? DefaultEncode(size, m);
        double[] decode = NumArray(dict["Decode"], resolve) ?? rng;
        return new SampledFunction(domain, rng, size, bps, encode, decode, data);
    }

    private static double[] DefaultEncode(int[] size, int m)
    {
        var e = new double[2 * m];
        for (int i = 0; i < m; i++)
        {
            e[2 * i] = 0.0;
            e[(2 * i) + 1] = (i < size.Length ? size[i] : 1) - 1;
        }

        return e;
    }

    protected override double[] EvaluateCore(double[] input)
    {
        int m = InputCount;
        int n = OutputCount;
        var outv = new double[n];
        if (m == 0 || n == 0 || _size.Length < m)
        {
            return outv;
        }

        // Encode each input to a grid coordinate in [0, size_i - 1].
        var e = new double[m];
        for (int i = 0; i < m; i++)
        {
            double ei = Interpolate(input[i], Domain[2 * i], Domain[(2 * i) + 1], _encode[2 * i], _encode[(2 * i) + 1]);
            e[i] = Clamp(ei, 0, _size[i] - 1);
        }

        // Multilinear interpolation over the 2^m surrounding grid corners.
        int corners = 1 << m;
        for (int c = 0; c < corners; c++)
        {
            double w = 1.0;
            long idx = 0;
            long stride = 1;
            for (int i = 0; i < m; i++)
            {
                int lo = (int)Math.Floor(e[i]);
                double f = e[i] - lo;
                int bit = (c >> i) & 1;
                int coord = lo + bit;
                if (coord > _size[i] - 1)
                {
                    coord = _size[i] - 1;
                }

                if (coord < 0)
                {
                    coord = 0;
                }

                w *= bit == 1 ? f : (1.0 - f);
                idx += coord * stride;
                stride *= _size[i];
            }

            if (w == 0.0)
            {
                continue;
            }

            for (int j = 0; j < n; j++)
            {
                double s = ReadSample((idx * n) + j);
                double dec = Interpolate(s, 0.0, _maxSample, _decode[2 * j], _decode[(2 * j) + 1]);
                outv[j] += w * dec;
            }
        }

        return outv;
    }

    private double ReadSample(long sampleIndex)
    {
        long bitPos = sampleIndex * _bps;
        long v = 0;
        for (int i = 0; i < _bps; i++)
        {
            long bp = bitPos + i;
            int bytePos = (int)(bp >> 3);
            int bit = 7 - (int)(bp & 7);
            int b = bytePos >= 0 && bytePos < _samples.Length ? (_samples[bytePos] >> bit) & 1 : 0;
            v = (v << 1) | (uint)b;
        }

        return v;
    }
}
