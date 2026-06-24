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

                images.Add(new PdfImageInfo
                {
                    PageIndex = page.Index,
                    DeclaredWidth = IntOf(document, image.Dictionary, "Width"),
                    DeclaredHeight = IntOf(document, image.Dictionary, "Height"),
                    Filter = FilterOf(image.Dictionary),
                    EncodedData = image.GetRawBytes(),
                });
            }
        }

        return images;
    }

    private static int IntOf(PdfDocument document, PdfDictionary dict, string key)
        => document.Resolve(dict[key] ?? PdfNull.Instance) is PdfNumber n ? (int)n.AsInt64 : 0;

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
