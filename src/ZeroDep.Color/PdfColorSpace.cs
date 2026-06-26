using System;
using ZeroDep.Objects;

namespace ZeroDep.Color;

/// <summary>
/// A resolved PDF color space (ISO 32000-2 §8.6) that maps an N-component sample tuple to 8-bit RGB.
/// Supports DeviceGray/RGB/CMYK, CalGray/CalRGB (approximated), Lab, ICCBased (pragmatic — by component
/// count and <c>/Alternate</c>, no profile parsing), Indexed (palette lookup), and Separation/DeviceN
/// (tint transform via <see cref="PdfFunction"/>). Pure-BCL; output is "correct-enough" sRGB, not a CMM.
/// </summary>
public abstract class PdfColorSpace
{
    /// <summary>Number of colour components a sample tuple carries in this space.</summary>
    public abstract int ComponentCount { get; }

    /// <summary>The colour-space family name (e.g. <c>DeviceRGB</c>, <c>Indexed</c>, <c>Separation</c>).</summary>
    public abstract string Family { get; }

    /// <summary>True for achromatic single-component spaces that can be emitted as 8-bit grey (DeviceGray/CalGray).</summary>
    internal virtual bool IsGrayscale => false;

    /// <summary>
    /// The default <c>/Decode</c> bounds (flat [min0,max0, …]) used to map raw samples to component values
    /// when the image supplies no explicit <c>/Decode</c>. Depends on <paramref name="bitsPerComponent"/>
    /// only for Indexed (index range 0…2^bpc−1).
    /// </summary>
    /// <param name="bitsPerComponent">The image's bits per component.</param>
    public abstract double[] DefaultDecode(int bitsPerComponent);

    /// <summary>Maps component values (already mapped through <c>/Decode</c>) to 8-bit RGB.</summary>
    /// <param name="comps">Buffer holding the component values.</param>
    /// <param name="offset">Index of the first component in <paramref name="comps"/>.</param>
    /// <param name="r">Red output (0–255).</param>
    /// <param name="g">Green output (0–255).</param>
    /// <param name="b">Blue output (0–255).</param>
    public abstract void ToRgb(double[] comps, int offset, out byte r, out byte g, out byte b);

    /// <summary>
    /// Resolves a PDF <c>/ColorSpace</c> object (name, array, or a name referencing a resource) into a
    /// <see cref="PdfColorSpace"/>. Throws <see cref="NotSupportedException"/> for Pattern / unknown spaces.
    /// </summary>
    /// <param name="obj">The color-space object (may be an indirect reference).</param>
    /// <param name="resolve">Resolves indirect references.</param>
    /// <param name="decodeStream">Decodes a stream's filters (for ICCBased, Indexed lookup, tint streams).</param>
    /// <param name="namedLookup">Resolves a colour-space name against the page resources, or null.</param>
    public static PdfColorSpace Resolve(
        PdfObject obj,
        Func<PdfObject, PdfObject> resolve,
        Func<PdfStream, byte[]> decodeStream,
        Func<string, PdfObject?>? namedLookup = null)
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

        if (ro is PdfName name)
        {
            switch (name.Value)
            {
                case "DeviceGray":
                case "G":
                    return DeviceGray.Instance;
                case "DeviceRGB":
                case "RGB":
                    return DeviceRgb.Instance;
                case "DeviceCMYK":
                case "CMYK":
                    return DeviceCmyk.Instance;
                case "Pattern":
                    throw new NotSupportedException("Pattern color space is out of scope.");
                default:
                    PdfObject? named = namedLookup?.Invoke(name.Value);
                    if (named is not null && !ReferenceEquals(resolve(named), ro))
                    {
                        return Resolve(named, resolve, decodeStream, namedLookup);
                    }

                    throw new NotSupportedException($"Unknown color space '{name.Value}'.");
            }
        }

        if (ro is PdfArray array && array.Count > 0 && resolve(array[0]) is PdfName family)
        {
            return ResolveArray(family.Value, array, resolve, decodeStream, namedLookup);
        }

        throw new NotSupportedException("Unsupported color space object.");
    }

    private static PdfColorSpace ResolveArray(
        string family,
        PdfArray array,
        Func<PdfObject, PdfObject> resolve,
        Func<PdfStream, byte[]> decodeStream,
        Func<string, PdfObject?>? namedLookup)
    {
        switch (family)
        {
            case "ICCBased":
                return ResolveIcc(array, resolve, decodeStream, namedLookup);

            case "CalGray":
                return DeviceGray.Instance; // approximate (gamma ignored)

            case "CalRGB":
                return DeviceRgb.Instance; // approximate (gamma/matrix ignored)

            case "Lab":
                return LabColorSpace.Create(array.Count > 1 ? resolve(array[1]) : PdfNull.Instance, resolve);

            case "Indexed":
            case "I":
                return IndexedColorSpace.Create(array, resolve, decodeStream, namedLookup);

            case "Separation":
                return SeparationColorSpace.Create(array, 1, resolve, decodeStream, namedLookup);

            case "DeviceN":
                return SeparationColorSpace.CreateDeviceN(array, resolve, decodeStream, namedLookup);

            case "Pattern":
                throw new NotSupportedException("Pattern color space is out of scope.");

            default:
                throw new NotSupportedException($"Unknown color space family '{family}'.");
        }
    }

    private static PdfColorSpace ResolveIcc(
        PdfArray array,
        Func<PdfObject, PdfObject> resolve,
        Func<PdfStream, byte[]> decodeStream,
        Func<string, PdfObject?>? namedLookup)
    {
        // Pragmatic: use /Alternate when present, else map by /N (no profile parsing).
        if (array.Count > 1 && resolve(array[1]) is PdfStream st)
        {
            PdfDictionary d = st.Dictionary;
            if (d["Alternate"] is { } alt)
            {
                try
                {
                    return Resolve(alt, resolve, decodeStream, namedLookup);
                }
                catch (NotSupportedException)
                {
                    // fall through to N-based mapping
                }
            }

            int n = resolve(d["N"] ?? PdfNull.Instance) is PdfNumber num ? (int)num.AsInt64 : 3;
            return ByComponentCount(n);
        }

        return DeviceRgb.Instance;
    }

    private protected static PdfColorSpace ByComponentCount(int n)
        => n switch
        {
            1 => DeviceGray.Instance,
            4 => DeviceCmyk.Instance,
            _ => DeviceRgb.Instance,
        };

    private protected static byte ToByte(double v01)
    {
        int x = (int)Math.Round(v01 * 255.0, MidpointRounding.AwayFromZero);
        return (byte)(x < 0 ? 0 : (x > 255 ? 255 : x));
    }
}

/// <summary>DeviceGray — a single [0,1] component rendered as neutral grey.</summary>
internal sealed class DeviceGray : PdfColorSpace
{
    public static DeviceGray Instance { get; } = new DeviceGray();

    public override int ComponentCount => 1;

    public override string Family => "DeviceGray";

    internal override bool IsGrayscale => true;

    public override double[] DefaultDecode(int bitsPerComponent) => new[] { 0.0, 1.0 };

    public override void ToRgb(double[] comps, int offset, out byte r, out byte g, out byte b)
    {
        byte v = ToByte(comps[offset]);
        r = g = b = v;
    }
}

/// <summary>DeviceRGB — three [0,1] components.</summary>
internal sealed class DeviceRgb : PdfColorSpace
{
    public static DeviceRgb Instance { get; } = new DeviceRgb();

    public override int ComponentCount => 3;

    public override string Family => "DeviceRGB";

    public override double[] DefaultDecode(int bitsPerComponent) => new[] { 0.0, 1.0, 0.0, 1.0, 0.0, 1.0 };

    public override void ToRgb(double[] comps, int offset, out byte r, out byte g, out byte b)
    {
        r = ToByte(comps[offset]);
        g = ToByte(comps[offset + 1]);
        b = ToByte(comps[offset + 2]);
    }
}

/// <summary>DeviceCMYK — four [0,1] inks, converted with the Adobe-style polynomial (ADR-0004 §7-6).</summary>
internal sealed class DeviceCmyk : PdfColorSpace
{
    public static DeviceCmyk Instance { get; } = new DeviceCmyk();

    public override int ComponentCount => 4;

    public override string Family => "DeviceCMYK";

    public override double[] DefaultDecode(int bitsPerComponent) => new[] { 0.0, 1.0, 0.0, 1.0, 0.0, 1.0, 0.0, 1.0 };

    public override void ToRgb(double[] comps, int offset, out byte r, out byte g, out byte b)
    {
        double c = comps[offset];
        double m = comps[offset + 1];
        double y = comps[offset + 2];
        double k = comps[offset + 3];

        // Adobe-derived polynomial (as used by pdf.js); inputs in [0,1], output 0..255.
        double red = 255.0 +
            (c * ((-4.387332384609988 * c) + (54.48615194189176 * m) + (18.82290502165302 * y) + (212.25662451639585 * k) - 285.2331026137004)) +
            (m * ((1.7149763477362134 * m) - (5.6096736904047315 * y) - (17.873870861415444 * k) - 5.497006427196366)) +
            (y * ((-2.5217340131683033 * y) - (21.248923337353073 * k) + 17.5119270841813)) +
            (k * ((-21.86122147463605 * k) - 189.48180835922747));

        double green = 255.0 +
            (c * ((8.841041422036149 * c) + (60.118027045597366 * m) + (6.871425592049007 * y) + (31.159100130055922 * k) - 79.2970844816548)) +
            (m * ((-15.310361306967817 * m) + (17.575251261109482 * y) + (131.35250912493976 * k) - 190.9453302588951)) +
            (y * ((4.444339102852739 * y) + (9.8632861493405 * k) - 24.86741582555878)) +
            (k * ((-20.737325471181034 * k) - 187.80453709719578));

        double blue = 255.0 +
            (c * ((0.8842522430003296 * c) + (8.078677503112928 * m) + (30.89978309703729 * y) - (0.23883238689178934 * k) - 14.183576799673286)) +
            (m * ((10.49593273432072 * m) + (63.02378494754052 * y) + (50.606957656360734 * k) - 112.23884253719248)) +
            (y * ((0.03296041114873217 * y) + (115.60384449646641 * k) - 193.58209356861505)) +
            (k * ((-22.33816807309886 * k) - 180.12613974708367));

        r = ClampByte(red);
        g = ClampByte(green);
        b = ClampByte(blue);
    }

    private static byte ClampByte(double v)
    {
        int x = (int)Math.Round(v, MidpointRounding.AwayFromZero);
        return (byte)(x < 0 ? 0 : (x > 255 ? 255 : x));
    }
}

/// <summary>CIE L*a*b* → sRGB (pragmatic: standard Lab→XYZ→sRGB, white-point adaptation omitted).</summary>
internal sealed class LabColorSpace : PdfColorSpace
{
    private readonly double _xn;
    private readonly double _yn;
    private readonly double _zn;
    private readonly double _aMin;
    private readonly double _aMax;
    private readonly double _bMin;
    private readonly double _bMax;

    private LabColorSpace(double xn, double yn, double zn, double aMin, double aMax, double bMin, double bMax)
    {
        _xn = xn;
        _yn = yn;
        _zn = zn;
        _aMin = aMin;
        _aMax = aMax;
        _bMin = bMin;
        _bMax = bMax;
    }

    public override int ComponentCount => 3;

    public override string Family => "Lab";

    public override double[] DefaultDecode(int bitsPerComponent) => new[] { 0.0, 100.0, _aMin, _aMax, _bMin, _bMax };

    public static LabColorSpace Create(PdfObject dictObj, Func<PdfObject, PdfObject> resolve)
    {
        double xn = 0.9505, yn = 1.0, zn = 1.089; // D65 default
        double aMin = -100, aMax = 100, bMin = -100, bMax = 100;

        if (resolve(dictObj) is PdfDictionary dict)
        {
            if (resolve(dict["WhitePoint"] ?? PdfNull.Instance) is PdfArray wp && wp.Count == 3)
            {
                xn = AsD(resolve(wp[0]));
                yn = AsD(resolve(wp[1]));
                zn = AsD(resolve(wp[2]));
            }

            if (resolve(dict["Range"] ?? PdfNull.Instance) is PdfArray rg && rg.Count == 4)
            {
                aMin = AsD(resolve(rg[0]));
                aMax = AsD(resolve(rg[1]));
                bMin = AsD(resolve(rg[2]));
                bMax = AsD(resolve(rg[3]));
            }
        }

        return new LabColorSpace(xn, yn, zn, aMin, aMax, bMin, bMax);
    }

    public override void ToRgb(double[] comps, int offset, out byte r, out byte g, out byte b)
    {
        double l = comps[offset];
        double a = comps[offset + 1];
        double bb = comps[offset + 2];

        double fy = (l + 16.0) / 116.0;
        double fx = fy + (a / 500.0);
        double fz = fy - (bb / 200.0);

        double x = _xn * Finv(fx);
        double y = _yn * Finv(fy);
        double z = _zn * Finv(fz);

        double rl = (3.2406 * x) - (1.5372 * y) - (0.4986 * z);
        double gl = (-0.9689 * x) + (1.8758 * y) + (0.0415 * z);
        double bl = (0.0557 * x) - (0.2040 * y) + (1.0570 * z);

        r = ToByte(Gamma(rl));
        g = ToByte(Gamma(gl));
        b = ToByte(Gamma(bl));
    }

    private static double Finv(double t)
    {
        const double D = 6.0 / 29.0;
        return t > D ? (t * t * t) : (3.0 * D * D * (t - (4.0 / 29.0)));
    }

    private static double Gamma(double c)
    {
        if (c <= 0.0)
        {
            return 0.0;
        }

        if (c >= 1.0)
        {
            return 1.0;
        }

        return c <= 0.0031308 ? (12.92 * c) : ((1.055 * Math.Pow(c, 1.0 / 2.4)) - 0.055);
    }

    private static double AsD(PdfObject o) => o is PdfNumber n ? n.AsDouble : 0.0;
}

/// <summary>Indexed — a single index into a precomputed RGB palette derived from the base space (§8.6.6.3).</summary>
internal sealed class IndexedColorSpace : PdfColorSpace
{
    private readonly byte[] _palette; // (hival+1) * 3
    private readonly int _hival;

    private IndexedColorSpace(byte[] palette, int hival)
    {
        _palette = palette;
        _hival = hival;
    }

    public override int ComponentCount => 1;

    public override string Family => "Indexed";

    public override double[] DefaultDecode(int bitsPerComponent)
        => new[] { 0.0, (1 << bitsPerComponent) - 1.0 };

    public static IndexedColorSpace Create(
        PdfArray array,
        Func<PdfObject, PdfObject> resolve,
        Func<PdfStream, byte[]> decodeStream,
        Func<string, PdfObject?>? namedLookup)
    {
        PdfColorSpace baseCs = Resolve(array[1], resolve, decodeStream, namedLookup);
        int hival = resolve(array[2]) is PdfNumber n ? (int)n.AsInt64 : 0;

        byte[] lookup = ReadLookup(resolve(array[3]), decodeStream);
        int baseComps = baseCs.ComponentCount;
        double[] baseDecode = baseCs.DefaultDecode(8);

        var palette = new byte[(hival + 1) * 3];
        var comp = new double[baseComps];
        for (int i = 0; i <= hival; i++)
        {
            for (int k = 0; k < baseComps; k++)
            {
                int li = (i * baseComps) + k;
                double sample = li < lookup.Length ? lookup[li] : 0.0;
                double lo = baseDecode[2 * k];
                double hi = baseDecode[(2 * k) + 1];
                comp[k] = lo + ((sample / 255.0) * (hi - lo));
            }

            baseCs.ToRgb(comp, 0, out byte r, out byte g, out byte b);
            palette[(i * 3) + 0] = r;
            palette[(i * 3) + 1] = g;
            palette[(i * 3) + 2] = b;
        }

        return new IndexedColorSpace(palette, hival);
    }

    public override void ToRgb(double[] comps, int offset, out byte r, out byte g, out byte b)
    {
        int idx = (int)Math.Round(comps[offset], MidpointRounding.AwayFromZero);
        if (idx < 0)
        {
            idx = 0;
        }
        else if (idx > _hival)
        {
            idx = _hival;
        }

        r = _palette[(idx * 3) + 0];
        g = _palette[(idx * 3) + 1];
        b = _palette[(idx * 3) + 2];
    }

    private static byte[] ReadLookup(PdfObject lookupObj, Func<PdfStream, byte[]> decodeStream)
    {
        return lookupObj switch
        {
            PdfString s => s.ToArray(),
            PdfStream st => decodeStream(st),
            _ => Array.Empty<byte>(),
        };
    }
}

/// <summary>Separation / DeviceN — n tint colorants mapped through a tint transform to an alternate space.</summary>
internal sealed class SeparationColorSpace : PdfColorSpace
{
    private readonly int _n;
    private readonly PdfColorSpace _alternate;
    private readonly PdfFunction _tint;
    private readonly string _family;

    private SeparationColorSpace(int n, PdfColorSpace alternate, PdfFunction tint, string family)
    {
        _n = n;
        _alternate = alternate;
        _tint = tint;
        _family = family;
    }

    public override int ComponentCount => _n;

    public override string Family => _family;

    public override double[] DefaultDecode(int bitsPerComponent)
    {
        var d = new double[2 * _n];
        for (int i = 0; i < _n; i++)
        {
            d[2 * i] = 0.0;
            d[(2 * i) + 1] = 1.0;
        }

        return d;
    }

    public static SeparationColorSpace Create(
        PdfArray array, int n, Func<PdfObject, PdfObject> resolve, Func<PdfStream, byte[]> decodeStream, Func<string, PdfObject?>? namedLookup)
    {
        // [/Separation name alternateSpace tintTransform]
        PdfColorSpace alt = Resolve(array[2], resolve, decodeStream, namedLookup);
        PdfFunction tint = PdfFunction.Parse(array[3], resolve, decodeStream);
        return new SeparationColorSpace(n, alt, tint, "Separation");
    }

    public static SeparationColorSpace CreateDeviceN(
        PdfArray array, Func<PdfObject, PdfObject> resolve, Func<PdfStream, byte[]> decodeStream, Func<string, PdfObject?>? namedLookup)
    {
        // [/DeviceN names alternateSpace tintTransform attributes?]
        int n = resolve(array[1]) is PdfArray names ? names.Count : 1;
        PdfColorSpace alt = Resolve(array[2], resolve, decodeStream, namedLookup);
        PdfFunction tint = PdfFunction.Parse(array[3], resolve, decodeStream);
        return new SeparationColorSpace(n, alt, tint, "DeviceN");
    }

    public override void ToRgb(double[] comps, int offset, out byte r, out byte g, out byte b)
    {
        var tintIn = new double[_n];
        for (int i = 0; i < _n; i++)
        {
            tintIn[i] = comps[offset + i];
        }

        double[] alt = _tint.Evaluate(tintIn);
        _alternate.ToRgb(alt, 0, out r, out g, out b);
    }
}
