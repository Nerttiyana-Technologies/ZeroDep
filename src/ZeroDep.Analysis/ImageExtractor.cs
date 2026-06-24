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

                images.Add(new PdfImageInfo
                {
                    PageIndex = page.Index,
                    DeclaredWidth = width,
                    DeclaredHeight = height,
                    Filter = filter,
                    Ccitt = filter == "CCITTFaxDecode" ? CcittOf(document, image.Dictionary, width, height) : null,
                    EncodedData = image.GetRawBytes(),
                });
            }
        }

        return images;
    }

    private static int IntOf(PdfDocument document, PdfDictionary dict, string key)
        => document.Resolve(dict[key] ?? PdfNull.Instance) is PdfNumber n ? (int)n.AsInt64 : 0;

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
