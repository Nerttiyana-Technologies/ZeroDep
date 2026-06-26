using System;
using System.Collections.Generic;
using System.IO;
using ZeroDep.Abstractions;
using ZeroDep.Model;
using ZeroDep.Objects;

namespace ZeroDep.Analysis;

/// <summary>
/// Enumerates page-level image XObjects and returns their encoded bytes. Used to feed the JPEG
/// decoder real <c>/DCTDecode</c> streams (for validation and OCR).
/// </summary>
internal static class ImageExtractor
{
    private static readonly PdfDictionary Empty = new PdfDictionary(new Dictionary<string, PdfObject>());

    public static IReadOnlyList<PdfImageInfo> Extract(Stream stream, string? password = null)
    {
        using PdfDocument document = PdfDocument.Open(stream, password);
        var images = new List<PdfImageInfo>();

        foreach (PdfPage page in document.Pages)
        {
            PdfDictionary resources = page.Resources ?? Empty;
            if (document.Resolve(resources["XObject"] ?? PdfNull.Instance) is not PdfDictionary xobjects)
            {
                continue;
            }

            foreach (string key in xobjects.Keys)
            {
                if (document.Resolve(xobjects[key] ?? PdfNull.Instance) is not PdfStream image)
                {
                    continue;
                }

                if (document.Resolve(image.Dictionary["Subtype"] ?? PdfNull.Instance) is not PdfName subtype
                    || subtype.Value != "Image")
                {
                    continue;
                }

                string? filter = FilterOf(image.Dictionary);
                int width = IntOf(document, image.Dictionary, "Width");
                int height = IntOf(document, image.Dictionary, "Height");
                (string? csFamily, int csComponents) = ColorSpaceOf(document, image.Dictionary, resources, 0);

                images.Add(new PdfImageInfo
                {
                    PageIndex = page.Index,
                    DeclaredWidth = width,
                    DeclaredHeight = height,
                    Filter = filter,
                    Ccitt = filter == "CCITTFaxDecode" ? CcittOf(document, image.Dictionary, width, height) : null,
                    Jbig2Globals = filter == "JBIG2Decode" ? Jbig2GlobalsOf(document, image.Dictionary) : null,
                    EncodedData = image.GetRawBytes(),
                    BitsPerComponent = image.Dictionary["BitsPerComponent"] is not null
                        ? IntOf(document, image.Dictionary, "BitsPerComponent")
                        : IntOf(document, image.Dictionary, "BPC"),
                    ColorSpaceFamily = csFamily,
                    ColorComponents = csComponents,
                    Decode = DecodeOf(document, image.Dictionary),
                    HasSoftMask = document.Resolve((image.Dictionary["SMask"] ?? PdfNull.Instance)) is PdfStream,
                });
            }
        }

        return images;
    }

    private static int IntOf(PdfDocument document, PdfDictionary dict, string key)
        => document.Resolve(dict[key] ?? PdfNull.Instance) is PdfNumber n ? (int)n.AsInt64 : 0;

    // Light colour-space probe: resolves the family + component count without building palettes/functions.
    private static (string? Family, int Components) ColorSpaceOf(
        PdfDocument document, PdfDictionary imageDict, PdfDictionary resources, int depth)
    {
        if (imageDict.ContainsKey("ImageMask") && document.Resolve(imageDict["ImageMask"] ?? PdfNull.Instance) is PdfBoolean { Value: true })
        {
            return ("ImageMask", 1);
        }

        return ProbeColorSpace(document, imageDict["ColorSpace"] ?? imageDict["CS"], resources, depth);
    }

    private static (string? Family, int Components) ProbeColorSpace(
        PdfDocument document, PdfObject? csObj, PdfDictionary resources, int depth)
    {
        if (depth > 8)
        {
            return (null, 0);
        }

        PdfObject ro = document.Resolve(csObj ?? PdfNull.Instance);

        if (ro is PdfName name)
        {
            switch (name.Value)
            {
                case "DeviceGray":
                case "G":
                case "CalGray":
                    return ("DeviceGray", 1);
                case "DeviceRGB":
                case "RGB":
                case "CalRGB":
                    return ("DeviceRGB", 3);
                case "DeviceCMYK":
                case "CMYK":
                    return ("DeviceCMYK", 4);
                case "Pattern":
                    return ("Pattern", 0);
                default:
                    if (document.Resolve(resources["ColorSpace"] ?? PdfNull.Instance) is PdfDictionary csDict
                        && csDict[name.Value] is { } named)
                    {
                        return ProbeColorSpace(document, named, resources, depth + 1);
                    }

                    return (name.Value, 0);
            }
        }

        if (ro is PdfArray array && array.Count > 0 && document.Resolve(array[0]) is PdfName family)
        {
            switch (family.Value)
            {
                case "ICCBased":
                {
                    int n = document.Resolve(array.Count > 1 ? array[1] : PdfNull.Instance) is PdfStream st
                        && document.Resolve(st.Dictionary["N"] ?? PdfNull.Instance) is PdfNumber num
                            ? (int)num.AsInt64
                            : 0;
                    return ("ICCBased", n);
                }

                case "Indexed":
                case "I":
                    return ("Indexed", 1);
                case "CalGray":
                    return ("DeviceGray", 1);
                case "CalRGB":
                    return ("DeviceRGB", 3);
                case "Lab":
                    return ("Lab", 3);
                case "Separation":
                    return ("Separation", 1);
                case "DeviceN":
                    return ("DeviceN", document.Resolve(array.Count > 1 ? array[1] : PdfNull.Instance) is PdfArray names ? names.Count : 1);
                case "Pattern":
                    return ("Pattern", 0);
                default:
                    return (family.Value, 0);
            }
        }

        return (null, 0);
    }

    private static double[]? DecodeOf(PdfDocument document, PdfDictionary imageDict)
    {
        if (document.Resolve(imageDict["Decode"] ?? imageDict["D"] ?? PdfNull.Instance) is not PdfArray array)
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

    private static CcittParameters CcittOf(PdfDocument document, PdfDictionary imageDict, int width, int height)
    {
        // /DecodeParms may be a dictionary, or an array aligned with the /Filter array (use the entry
        // that actually carries CCITT keys). Defaults per ISO 32000-2 §7.4.6.
        PdfDictionary parms = FindCcittParms(document, imageDict["DecodeParms"] ?? imageDict["DP"]);

        int columns = parms["Columns"] is not null ? IntOf(document, parms, "Columns") : (width > 0 ? width : 1728);
        int rows = parms["Rows"] is not null ? IntOf(document, parms, "Rows") : height;

        return new CcittParameters
        {
            K = IntOf(document, parms, "K"),
            Columns = columns,
            Rows = rows,
            BlackIs1 = BoolOf(document, parms, "BlackIs1"),
            EncodedByteAlign = BoolOf(document, parms, "EncodedByteAlign"),
        };
    }

    private static PdfDictionary FindCcittParms(PdfDocument document, PdfObject? parms)
    {
        switch (document.Resolve(parms ?? PdfNull.Instance))
        {
            case PdfDictionary dict:
                return dict;
            case PdfArray array:
                foreach (PdfObject item in array.Items)
                {
                    if (document.Resolve(item) is PdfDictionary d && d["K"] is not null)
                    {
                        return d;
                    }
                }

                break;
        }

        return Empty;
    }

    private static bool BoolOf(PdfDocument document, PdfDictionary dict, string key)
        => document.Resolve(dict[key] ?? PdfNull.Instance) is PdfBoolean b && b.Value;

    private static byte[]? Jbig2GlobalsOf(PdfDocument document, PdfDictionary imageDict)
    {
        // /DecodeParms may be a dictionary or an array (filter chain); find the one with JBIG2Globals.
        foreach (PdfDictionary parms in ParmDicts(document, imageDict["DecodeParms"] ?? imageDict["DP"]))
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

    private static string? FilterOf(PdfDictionary dict)
    {
        PdfObject? filter = dict["Filter"];
        if (filter is PdfName name)
        {
            return name.Value;
        }

        if (filter is PdfArray array)
        {
            var parts = new List<string>();
            foreach (PdfObject item in array.Items)
            {
                if (item is PdfName n)
                {
                    parts.Add(n.Value);
                }
            }

            return parts.Count > 0 ? string.Join("+", parts) : null;
        }

        return null;
    }
}
