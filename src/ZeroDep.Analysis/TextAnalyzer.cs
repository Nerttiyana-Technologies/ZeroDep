using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ZeroDep.Abstractions;
using ZeroDep.Content;
using ZeroDep.Model;
using ZeroDep.Objects;

namespace ZeroDep.Analysis;

/// <summary>
/// Feature B: extracts positioned text runs from page content streams (BT/ET, Tj/TJ/'/"),
/// tagging the invisible OCR layer (Tr = 3). ISO 32000-2 §9.4.
/// </summary>
internal static class TextAnalyzer
{
    private static readonly PdfDictionary EmptyResources = new PdfDictionary(new Dictionary<string, PdfObject>());

    /// <summary>Opens a PDF stream and extracts all text runs.</summary>
    public static IReadOnlyList<TextRunInfo> Analyze(Stream stream, string? password = null)
    {
        using PdfDocument document = PdfDocument.Open(stream, password);
        return Analyze(document);
    }

    /// <summary>Extracts all text runs from an open document.</summary>
    public static IReadOnlyList<TextRunInfo> Analyze(PdfDocument document)
        => AnalyzeInternal(document, out _);

    /// <summary>
    /// Extracts text runs and, alongside, the per-page structural signals gathered in the same content
    /// pass (ruling-line count, distinct-font count) for per-page classification (ADR-0003).
    /// </summary>
    public static IReadOnlyList<TextRunInfo> AnalyzeWithStructure(PdfDocument document, out IReadOnlyList<PageStructure> pageStructures)
        => AnalyzeInternal(document, out pageStructures);

    private static IReadOnlyList<TextRunInfo> AnalyzeInternal(PdfDocument document, out IReadOnlyList<PageStructure> pageStructures)
    {
        var runs = new List<TextRunInfo>();
        var structures = new List<PageStructure>();
        var interpreter = new ContentInterpreter(document.Resolve, StreamDecoder.Decode);

        foreach (PdfPage page in document.Pages)
        {
            byte[] content = DecodeContents(document, page);
            if (content.Length == 0) continue;

            PdfDictionary resources = page.Resources ?? EmptyResources;
            ContentResult result;
            try
            {
                result = interpreter.RunAll(content, resources, Matrix.Identity);
            }
            catch
            {
                continue;
            }

            foreach (TextRun run in result.TextRuns)
            {
                runs.Add(new TextRunInfo
                {
                    PageIndex = page.Index,
                    Text = run.Text,
                    X = run.X,
                    Width = run.Width,
                    Y = run.Y,
                    FontSize = run.FontSize,
                    RenderMode = run.RenderMode,
                    IsOcrLayer = run.IsOcrLayer,
                    AuthoritativeChars = run.AuthoritativeChars,
                    FallbackChars = run.FallbackChars,
                    UnmappedChars = run.UnmappedChars,
                    SpaceWidthEm = run.SpaceWidthEm,
                });
            }

            structures.Add(new PageStructure(page.Index, result.RulingLineCount, result.FontNames.Count));
        }

        pageStructures = structures;
        return runs;
    }

    /// <summary>Opens a PDF stream and returns a simple, line-grouped plain-text rendering.</summary>
    public static string GetPlainText(Stream stream, string? password = null) => BuildPlainText(Analyze(stream, password));

    // Insert a space between runs when the positional gap exceeds this fraction of the font's space advance
    // (ADR-0008). 0.5 catches genuine one-space gaps while staying clear of tight intra-word kerning.
    private const double SpaceGapFraction = 0.5;

    private static bool StartsWithSpace(string s) => s.Length > 0 && char.IsWhiteSpace(s[0]);

    /// <summary>Joins text runs into plain text by grouping per page and per baseline.</summary>
    public static string BuildPlainText(IReadOnlyList<TextRunInfo> runs)
    {
        var sb = new StringBuilder();
        foreach (var page in runs.GroupBy(r => r.PageIndex).OrderBy(g => g.Key))
        {
            foreach (var line in page.GroupBy(r => Math.Round(r.Y)).OrderByDescending(g => g.Key))
            {
                double? previousRight = null;
                double previousSpace = 0.25;
                double previousFontSize = 0;
                bool previousEndsWithSpace = false;
                foreach (TextRunInfo run in line.OrderBy(r => r.X))
                {
                    if (previousRight.HasValue && !previousEndsWithSpace && !StartsWithSpace(run.Text))
                    {
                        double gap = run.X - previousRight.Value;
                        // A space is encoded positionally when the gap exceeds a fraction of the font's own
                        // space advance (ADR-0008). The font space width is the right yardstick — a flat
                        // 0.25*FontSize wrongly exceeds narrow space widths and drops genuine word breaks.
                        double fontSize = previousFontSize > 0 ? previousFontSize : (run.FontSize > 0 ? run.FontSize : 1);
                        double spaceEm = previousSpace > 0 ? previousSpace : 0.25;
                        double threshold = SpaceGapFraction * spaceEm * fontSize;
                        if (gap > threshold) sb.Append(' ');
                    }

                    sb.Append(run.Text);
                    previousRight = run.X + run.Width;
                    previousSpace = run.SpaceWidthEm;
                    previousFontSize = run.FontSize;
                    previousEndsWithSpace = run.Text.Length > 0 && char.IsWhiteSpace(run.Text[run.Text.Length - 1]);
                }
                sb.Append('\n');
            }
            sb.Append('\n');
        }
        return sb.ToString();
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
}
