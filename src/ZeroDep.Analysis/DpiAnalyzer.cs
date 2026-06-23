using System;
using System.Collections.Generic;
using System.IO;
using ZeroDep.Abstractions;
using ZeroDep.Content;
using ZeroDep.Model;
using ZeroDep.Objects;

namespace ZeroDep.Analysis;

/// <summary>
/// Feature A: computes the effective DPI of every placed raster image and flags
/// those below a configurable threshold (ISO 32000-2 §8.3.3, §8.9.5).
/// </summary>
internal static class DpiAnalyzer
{
    private static readonly PdfDictionary EmptyResources = new PdfDictionary(new Dictionary<string, PdfObject>());

    /// <summary>Opens a PDF stream and analyzes image DPI across all pages.</summary>
    public static IReadOnlyList<ImageDpiInfo> Analyze(Stream stream, int threshold, string? password = null)
    {
        using PdfDocument document = PdfDocument.Open(stream, password);
        return Analyze(document, threshold);
    }

    /// <summary>Analyzes image DPI across all pages of an open document.</summary>
    public static IReadOnlyList<ImageDpiInfo> Analyze(PdfDocument document, int threshold)
    {
        var results = new List<ImageDpiInfo>();
        var interpreter = new ContentInterpreter(document.Resolve, StreamDecoder.Decode);

        foreach (PdfPage page in document.Pages)
        {
            byte[] content = DecodeContents(document, page);
            if (content.Length == 0) continue;

            PdfDictionary resources = page.Resources ?? EmptyResources;
            List<ImagePlacement> placements;
            try
            {
                placements = interpreter.Run(content, resources, Matrix.Identity);
            }
            catch
            {
                continue; // a malformed page should not abort the whole document
            }

            double pageArea = page.MediaBox.Width * page.MediaBox.Height;
            foreach (ImagePlacement placement in placements)
            {
                results.Add(BuildInfo(document, page.Index, placement, threshold, pageArea));
            }
        }

        return results;
    }

    private static byte[] DecodeContents(PdfDocument document, PdfPage page)
    {
        PdfObject? contents = page.Dictionary["Contents"];
        if (contents is null) return Array.Empty<byte>();

        try
        {
            PdfObject resolved = document.Resolve(contents);
            if (resolved is PdfStream stream)
            {
                return StreamDecoder.Decode(stream);
            }
            if (resolved is PdfArray array)
            {
                using var buffer = new MemoryStream();
                foreach (PdfObject item in array.Items)
                {
                    if (document.Resolve(item) is PdfStream part)
                    {
                        byte[] decoded = StreamDecoder.Decode(part);
                        buffer.Write(decoded, 0, decoded.Length);
                        buffer.WriteByte((byte)'\n');
                    }
                }
                return buffer.ToArray();
            }
        }
        catch
        {
            return Array.Empty<byte>();
        }

        return Array.Empty<byte>();
    }

    private static ImageDpiInfo BuildInfo(PdfDocument document, int pageIndex, ImagePlacement placement, int threshold, double pageArea)
    {
        PdfDictionary dict = placement.Image.Dictionary;
        int pixelWidth = IntOf(document, dict, "Width");
        int pixelHeight = IntOf(document, dict, "Height");

        Matrix m = placement.Transform;
        double renderedWidth = Math.Sqrt((m.A * m.A) + (m.B * m.B));
        double renderedHeight = Math.Sqrt((m.C * m.C) + (m.D * m.D));

        double dpiX = renderedWidth > 0 ? pixelWidth * 72.0 / renderedWidth : 0;
        double dpiY = renderedHeight > 0 ? pixelHeight * 72.0 / renderedHeight : 0;
        double effective = EffectiveDpi(dpiX, dpiY);

        double areaFraction = pageArea > 0 ? Math.Min(1.0, (renderedWidth * renderedHeight) / pageArea) : 0;

        return new ImageDpiInfo
        {
            PageIndex = pageIndex,
            ResourceName = placement.Name,
            PixelWidth = pixelWidth,
            PixelHeight = pixelHeight,
            RenderedWidthPoints = renderedWidth,
            RenderedHeightPoints = renderedHeight,
            HorizontalDpi = dpiX,
            VerticalDpi = dpiY,
            EffectiveDpi = effective,
            Threshold = threshold,
            IsBelowThreshold = effective > 0 && effective < threshold,
            Filter = FilterOf(dict),
            HasSoftMask = dict.ContainsKey("SMask"),
            HasMask = dict.ContainsKey("Mask"),
            IsImageMask = dict["ImageMask"] is PdfBoolean mask && mask.Value,
            PageAreaFraction = areaFraction,
        };
    }

    private static double EffectiveDpi(double x, double y)
    {
        if (x > 0 && y > 0) return Math.Min(x, y);
        return Math.Max(x, y);
    }

    private static int IntOf(PdfDocument document, PdfDictionary dict, string key)
    {
        PdfObject? value = dict[key];
        if (value is null) return 0;
        return document.Resolve(value) is PdfNumber number ? (int)number.AsInt64 : 0;
    }

    private static string? FilterOf(PdfDictionary dict)
    {
        PdfObject? filter = dict["Filter"];
        if (filter is PdfName name) return name.Value;
        if (filter is PdfArray array)
        {
            var parts = new List<string>();
            foreach (PdfObject item in array.Items)
            {
                if (item is PdfName n) parts.Add(n.Value);
            }
            return parts.Count > 0 ? string.Join("+", parts) : null;
        }
        return null;
    }
}
