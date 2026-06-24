using System.Collections.Generic;
using System.IO;
using ZeroDep.Abstractions;
using ZeroDep.Analysis;
using ZeroDep.Json;

namespace ZeroDep;

/// <summary>Public entry point for ZeroDep structural analysis features.</summary>
public static class PdfAnalyzer
{
    /// <summary>Extracts the interactive form (AcroForm) fields from a PDF stream (Feature C).</summary>
    /// <param name="stream">A readable PDF stream.</param>
    /// <param name="password">Password for an encrypted document (empty/default if null).</param>
    public static AcroFormReport ExtractForm(Stream stream, string? password = null)
        => AcroFormAnalyzer.Analyze(stream, password);

    /// <summary>Computes effective DPI for every placed image and flags those below a threshold (Feature A).</summary>
    /// <param name="stream">A readable PDF stream.</param>
    /// <param name="dpiThreshold">Images with effective DPI below this value are flagged.</param>
    /// <param name="password">Password for an encrypted document (empty/default if null).</param>
    public static IReadOnlyList<ImageDpiInfo> AnalyzeImageDpi(Stream stream, int dpiThreshold = AnalyzerOptions.DefaultDpiThreshold, string? password = null)
        => DpiAnalyzer.Analyze(stream, dpiThreshold, password);

    /// <summary>Extracts positioned text runs (including the invisible OCR layer) from a PDF stream (Feature B).</summary>
    /// <param name="stream">A readable PDF stream.</param>
    /// <param name="password">Password for an encrypted document (empty/default if null).</param>
    public static IReadOnlyList<TextRunInfo> ExtractText(Stream stream, string? password = null)
        => TextAnalyzer.Analyze(stream, password);

    /// <summary>Extracts a simple line-grouped plain-text rendering of a PDF stream.</summary>
    /// <param name="stream">A readable PDF stream.</param>
    /// <param name="password">Password for an encrypted document (empty/default if null).</param>
    public static string ExtractPlainText(Stream stream, string? password = null)
        => TextAnalyzer.GetPlainText(stream, password);

    /// <summary>Runs all features and returns the unified document analysis (with coverage manifest).</summary>
    /// <param name="stream">A readable PDF stream.</param>
    /// <param name="dpiThreshold">Images with effective DPI below this value are flagged.</param>
    /// <param name="password">Password for an encrypted document (empty/default if null).</param>
    public static DocumentAnalysis Analyze(Stream stream, int dpiThreshold = AnalyzerOptions.DefaultDpiThreshold, string? password = null)
        => DocumentAnalyzer.Analyze(stream, dpiThreshold, password);

    /// <summary>Runs all features and serializes the result to the versioned JSON schema.</summary>
    /// <param name="stream">A readable PDF stream.</param>
    /// <param name="indent">Whether to pretty-print the JSON.</param>
    /// <param name="dpiThreshold">Images with effective DPI below this value are flagged.</param>
    /// <param name="password">Password for an encrypted document (empty/default if null).</param>
    public static string ToJson(Stream stream, bool indent = false, int dpiThreshold = AnalyzerOptions.DefaultDpiThreshold, string? password = null)
        => DocumentJson.Write(DocumentAnalyzer.Analyze(stream, dpiThreshold, password), indent);

    /// <summary>Enumerates the embedded image XObjects in a PDF, with their declared size, filter, and raw bytes.</summary>
    /// <param name="stream">A readable PDF stream.</param>
    /// <param name="password">Password for an encrypted document (empty/default if null).</param>
    public static IReadOnlyList<PdfImageInfo> ExtractImages(Stream stream, string? password = null)
        => ImageExtractor.Extract(stream, password);
}
