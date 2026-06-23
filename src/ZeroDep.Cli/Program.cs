using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ZeroDep;
using ZeroDep.Abstractions;
using ZeroDep.Batch;

if (args.Length > 0 && string.Equals(args[0], "batch", StringComparison.Ordinal))
{
    return await RunBatchAsync(args);
}

string? path = null;
int threshold = AnalyzerOptions.DefaultDpiThreshold;
bool json = false;
string? password = null;

foreach (string arg in args)
{
    if (arg == "--json") json = true;
    else if (arg.StartsWith("--password=", StringComparison.Ordinal)) password = arg.Substring("--password=".Length);
    else if (int.TryParse(arg, out int value)) threshold = value;
    else if (path is null) path = arg;
}

if (path is null)
{
    Console.Error.WriteLine("usage: zerodep <file.pdf> [dpiThreshold] [--json] [--password=PW]");
    return 1;
}
if (!File.Exists(path))
{
    Console.Error.WriteLine($"file not found: {path}");
    return 2;
}

if (json)
{
    using FileStream s = File.OpenRead(path);
    Console.WriteLine(PdfAnalyzer.ToJson(s, indent: true, threshold, password));
    return 0;
}

Console.WriteLine($"== {Path.GetFileName(path)} ==");

using (FileStream stream = File.OpenRead(path))
{
    var images = PdfAnalyzer.AnalyzeImageDpi(stream, threshold, password);
    int low = 0;
    foreach (ImageDpiInfo image in images)
    {
        if (image.IsBelowThreshold) low++;
    }
    Console.WriteLine($"\nImages: {images.Count}  (below {threshold} DPI: {low})");
    foreach (ImageDpiInfo image in images)
    {
        string flag = image.IsBelowThreshold ? "  [LOW]" : "";
        Console.WriteLine($"  p{image.PageIndex} {image.ResourceName,-10} {image.PixelWidth}x{image.PixelHeight}px  eff {image.EffectiveDpi:F0} DPI{flag}");
    }
}

using (FileStream stream = File.OpenRead(path))
{
    AcroFormReport form = PdfAnalyzer.ExtractForm(stream, password);
    Console.WriteLine($"\nAcroForm: {(form.HasAcroForm ? form.Fields.Count + " fields" : "none")}");
    foreach (FormFieldInfo field in form.Fields)
    {
        string value = field.IsChecked is bool b ? (b ? "[X]" : "[ ]") : Truncate(Clean(field.Value ?? ""), 60);
        string label = string.IsNullOrEmpty(field.Label) ? "" : "   (" + Truncate(Clean(field.Label!), 80) + ")";
        Console.WriteLine($"  [{field.FieldType,-3}] {field.FullyQualifiedName} = {value}{label}");
    }
}

using (FileStream stream = File.OpenRead(path))
{
    var runs = PdfAnalyzer.ExtractText(stream, password);
    int ocr = 0;
    foreach (TextRunInfo r in runs)
    {
        if (r.IsOcrLayer) ocr++;
    }
    Console.WriteLine($"\nText runs: {runs.Count}  (OCR-layer: {ocr})");
}

using (FileStream stream = File.OpenRead(path))
{
    string text = PdfAnalyzer.ExtractPlainText(stream, password);
    Console.WriteLine("\n--- text (first 30 non-empty lines) ---");
    int shown = 0;
    foreach (string line in text.Split('\n'))
    {
        string clean = Clean(line).Trim();
        if (clean.Length == 0) continue;
        Console.WriteLine(Truncate(clean, 100));
        if (++shown >= 30) break;
    }
}

return 0;

static string Clean(string s)
{
    var sb = new StringBuilder(s.Length);
    foreach (char c in s) sb.Append(char.IsControl(c) ? ' ' : c);
    return sb.ToString();
}

static string Truncate(string s, int max) => s.Length > max ? s.Substring(0, max) + "…" : s;

static async Task<int> RunBatchAsync(string[] args)
{
    string? input = null;
    string? output = null;
    int dpiThreshold = AnalyzerOptions.DefaultDpiThreshold;
    string? pw = null;
    bool resume = true;
    bool perFile = true;
    int concurrency = Environment.ProcessorCount;

    for (int i = 1; i < args.Length; i++)
    {
        string a = args[i];
        if (a.StartsWith("--output=", StringComparison.Ordinal)) output = a.Substring("--output=".Length);
        else if (a.StartsWith("--threshold=", StringComparison.Ordinal) && int.TryParse(a.Substring("--threshold=".Length), out int t)) dpiThreshold = t;
        else if (a.StartsWith("--password=", StringComparison.Ordinal)) pw = a.Substring("--password=".Length);
        else if (a.StartsWith("--concurrency=", StringComparison.Ordinal) && int.TryParse(a.Substring("--concurrency=".Length), out int c)) concurrency = c;
        else if (a == "--no-resume") resume = false;
        else if (a == "--no-perfile") perFile = false;
        else if (input is null) input = a;
    }

    if (input is null)
    {
        Console.Error.WriteLine("usage: zerodep batch <inputDir> [--output=DIR] [--threshold=N] [--password=PW] [--concurrency=N] [--no-resume] [--no-perfile]");
        return 1;
    }
    if (!Directory.Exists(input))
    {
        Console.Error.WriteLine($"directory not found: {input}");
        return 2;
    }

    output ??= Path.Combine("private", "batch-out");

    var options = new BatchOptions
    {
        InputDirectory = input,
        OutputDirectory = output,
        DpiThreshold = dpiThreshold,
        Password = pw,
        Resume = resume,
        WritePerFileJson = perFile,
        MaxConcurrency = concurrency,
    };

    Console.WriteLine($"Batch: scanning '{input}' (concurrency {concurrency}, resume {resume})...");
    var processor = new BatchProcessor();
    BatchSummary summary = await processor.RunAsync(options).ConfigureAwait(false);

    Console.WriteLine($"\nFiles: {summary.Total}   processed now: {summary.ProcessedThisRun}   skipped: {summary.Skipped}");
    Console.WriteLine($"Rejected: {summary.Rejected}   encrypted-unreadable: {summary.EncryptedUnreadable}");
    Console.WriteLine("\nCategories:");
    foreach (CategoryCount category in summary.Statistics.Categories)
    {
        Console.WriteLine($"  {category.Category,-20} {category.Count,7}   {category.Percent,5:F1}%");
    }

    Console.WriteLine($"\nAggregate statistics: {summary.AggregatePath}");
    return 0;
}
