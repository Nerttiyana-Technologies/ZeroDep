using System;
using System.Collections.Generic;
using System.IO;
using ZeroDep.Color;
using ZeroDep.Filters;
using ZeroDep.Filters.Jbig2;
using ZeroDep.Filters.Jpx;
using ZeroDep.Model;
using ZeroDep.Objects;

namespace ZeroDep.Analysis;

/// <summary>
/// A decoded, colour-normalized image: its source page and an RGB (3-component) or grayscale (1-component)
/// <see cref="RasterImage"/> produced by applying the image's PDF colour space (ADR-0004).
/// </summary>
public sealed class ColorImage
{
    /// <summary>The zero-based page index the image appears on.</summary>
    public int PageIndex { get; init; }

    /// <summary>The normalized pixels (RGB24, or Gray8 for DeviceGray/CalGray).</summary>
    public RasterImage Image { get; init; } = new RasterImage();

    /// <summary>The resolved colour-space family (e.g. <c>DeviceRGB</c>, <c>Indexed</c>), or null.</summary>
    public string? ColorSpaceFamily { get; init; }
}

/// <summary>
/// Decodes embedded images to colour-correct RGB by combining the pure-BCL image decoders (JPEG, CCITT,
/// JBIG2, JPEG 2000, plus Flate/LZW raster) with <see cref="ZeroDep.Color"/> colour-space normalization.
/// Codec output that is already colour-transformed (JPEG/JPX RGB) is passed through; Indexed images get
/// their palette applied; raw raster is unpacked and normalized through the resolved colour space.
/// </summary>
internal static class ColorImageDecoder
{
    private static readonly PdfDictionary Empty = new PdfDictionary(new Dictionary<string, PdfObject>());

    public static IReadOnlyList<ColorImage> Extract(
        Stream stream, string? password = null, int maxImages = int.MaxValue, Func<string?, bool>? shouldDecode = null)
    {
        using PdfDocument document = PdfDocument.Open(stream, password);
        var result = new List<ColorImage>();
        int attempted = 0;

        foreach (PdfPage page in document.Pages)
        {
            if (attempted >= maxImages)
            {
                break;
            }

            PdfDictionary resources = page.Resources ?? Empty;
            if (document.Resolve(resources["XObject"] ?? PdfNull.Instance) is not PdfDictionary xobjects)
            {
                continue;
            }

            foreach (string key in xobjects.Keys)
            {
                if (attempted >= maxImages)
                {
                    break;
                }

                if (document.Resolve(xobjects[key] ?? PdfNull.Instance) is not PdfStream image)
                {
                    continue;
                }

                if (document.Resolve(image.Dictionary["Subtype"] ?? PdfNull.Instance) is not PdfName subtype
                    || subtype.Value != "Image")
                {
                    continue;
                }

                attempted++;

                RasterImage? rgb;
                string? family;
                try
                {
                    (rgb, family) = DecodeImage(document, image, resources, shouldDecode);
                }
                catch (Exception)
                {
                    continue; // an undecodable image is isolated, never aborts the document
                }

                if (rgb is not null)
                {
                    result.Add(new ColorImage { PageIndex = page.Index, Image = rgb, ColorSpaceFamily = family });
                }
            }
        }

        return result;
    }

    private static (RasterImage? Image, string? Family) DecodeImage(
        PdfDocument document, PdfStream image, PdfDictionary resources, Func<string?, bool>? shouldDecode)
    {
        PdfDictionary dict = image.Dictionary;
        string? filter = FilterOf(dict);
        int w = IntOf(document, dict, "Width");
        int h = IntOf(document, dict, "Height");
        int bpc = dict["BitsPerComponent"] is not null ? IntOf(document, dict, "BitsPerComponent") : IntOf(document, dict, "BPC");
        double[]? decode = DecodeOf(document, dict);

        PdfColorSpace? cs = TryResolveColorSpace(document, dict, resources);
        string? family = cs?.Family;

        // Skip the (potentially expensive) decode when the caller does not want this family.
        if (shouldDecode is not null && !shouldDecode(family))
        {
            return (null, family);
        }

        // Apply the colour space + /Decode when the codec yielded raw components (count matches the colour
        // space) — Indexed palette, Separation/DeviceN tint, gray/RGB /Decode. When the codec already
        // colour-transformed (e.g. a CMYK JPEG collapsed to RGB), the counts differ and we pass through.
        RasterImage Normalize(RasterImage dec)
            => cs is not null && dec.Components == cs.ComponentCount ? ColorConverter.ToRgb(dec, cs, decode) : dec;

        switch (filter)
        {
            case "DCTDecode":
            {
                // Preserve CMYK (4-comp) so the colour space + /Decode apply (Adobe polynomial); a
                // 3-comp DeviceRGB/Gray JPEG is already colour-transformed.
                bool preserveCmyk = cs is not null && cs.ComponentCount == 4;
                return (Normalize(JpegDecoder.Decode(image.GetRawBytes(), preserveCmyk)), family);
            }

            case "CCITTFaxDecode":
                return (Normalize(CcittFaxDecode.Decode(image.GetRawBytes(), CcittParamsOf(document, dict, w, h))), family);

            case "JBIG2Decode":
                return (Normalize(Jbig2Decode.Decode(image.GetRawBytes(), Jbig2GlobalsOf(document, dict), w, h)), family);

            case "JPXDecode":
                return (Normalize(JpxDecode.Decode(image.GetRawBytes(), w, h)), family);

            default:
            {
                // Raw raster: Flate/LZW/ASCII filters (decoded by StreamDecoder) or uncompressed.
                if (cs is null || w <= 0 || h <= 0)
                {
                    return (null, family);
                }

                byte[] packed = StreamDecoder.Decode(image);
                return (ColorConverter.ToRgb(packed, w, h, cs.ComponentCount, bpc > 0 ? bpc : 8, cs, decode), family);
            }
        }
    }

    private static PdfColorSpace? TryResolveColorSpace(PdfDocument document, PdfDictionary dict, PdfDictionary resources)
    {
        PdfObject? csObj = dict["ColorSpace"] ?? dict["CS"];
        if (csObj is null)
        {
            return null;
        }

        try
        {
            PdfObject? NamedLookup(string n)
                => document.Resolve(resources["ColorSpace"] ?? PdfNull.Instance) is PdfDictionary d ? d[n] : null;

            return PdfColorSpace.Resolve(csObj, o => document.Resolve(o), s => StreamDecoder.Decode(s), NamedLookup);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static CcittParams CcittParamsOf(PdfDocument document, PdfDictionary dict, int width, int height)
    {
        PdfDictionary parms = Empty;
        foreach (PdfDictionary candidate in ParmDicts(document, dict["DecodeParms"] ?? dict["DP"]))
        {
            if (candidate["K"] is not null || candidate["Columns"] is not null || candidate["Rows"] is not null)
            {
                parms = candidate;
                break;
            }
        }

        int columns = parms["Columns"] is not null ? IntOf(document, parms, "Columns") : (width > 0 ? width : 1728);
        int rows = parms["Rows"] is not null ? IntOf(document, parms, "Rows") : height;

        return new CcittParams
        {
            K = IntOf(document, parms, "K"),
            Columns = columns,
            Rows = rows,
            BlackIs1 = BoolOf(document, parms, "BlackIs1"),
            EncodedByteAlign = BoolOf(document, parms, "EncodedByteAlign"),
        };
    }

    private static byte[]? Jbig2GlobalsOf(PdfDocument document, PdfDictionary dict)
    {
        foreach (PdfDictionary parms in ParmDicts(document, dict["DecodeParms"] ?? dict["DP"]))
        {
            if (document.Resolve(parms["JBIG2Globals"] ?? PdfNull.Instance) is PdfStream globals)
            {
                try
                {
                    return StreamDecoder.Decode(globals);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        return null;
    }

    private static IEnumerable<PdfDictionary> ParmDicts(PdfDocument document, PdfObject? parms)
    {
        switch (document.Resolve(parms ?? PdfNull.Instance))
        {
            case PdfDictionary dict:
                yield return dict;
                break;
            case PdfArray array:
                foreach (PdfObject item in array.Items)
                {
                    if (document.Resolve(item) is PdfDictionary d)
                    {
                        yield return d;
                    }
                }

                break;
        }
    }

    private static double[]? DecodeOf(PdfDocument document, PdfDictionary dict)
    {
        if (document.Resolve(dict["Decode"] ?? dict["D"] ?? PdfNull.Instance) is not PdfArray array)
        {
            return null;
        }

        var values = new double[array.Count];
        for (int i = 0; i < array.Count; i++)
        {
            values[i] = document.Resolve(array[i]) is PdfNumber n ? n.AsDouble : 0.0;
        }

        return values;
    }

    private static string? FilterOf(PdfDictionary dict)
    {
        switch (dict["Filter"] ?? dict["F"])
        {
            case PdfName name:
                return name.Value;
            case PdfArray array when array.Count > 0 && array[array.Count - 1] is PdfName last:
                return last.Value; // the image codec is the last filter in the chain
            default:
                return null;
        }
    }

    private static int IntOf(PdfDocument document, PdfDictionary dict, string key)
        => document.Resolve(dict[key] ?? PdfNull.Instance) is PdfNumber n ? (int)n.AsInt64 : 0;

    private static bool BoolOf(PdfDocument document, PdfDictionary dict, string key)
        => document.Resolve(dict[key] ?? PdfNull.Instance) is PdfBoolean b && b.Value;
}
