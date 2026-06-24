![ZeroDep — Zero External Dependencies. Pure PDF Processing.](https://raw.githubusercontent.com/Nerttiyana-Technologies/ZeroDep/main/assets/zerodep-header.png)

**Zero External Dependencies. Pure PDF Processing.**

A 100% dependency-free .NET engine for PDF structural analysis.

![Target frameworks](https://img.shields.io/badge/.NET-netstandard2.0%20%7C%20net8.0%20%7C%20net10.0-512BD4)
![Zero dependencies](https://img.shields.io/badge/dependencies-0-1ED760)
![License](https://img.shields.io/badge/license-Apache--2.0-blue)
![ISO 32000-2](https://img.shields.io/badge/PDF-ISO%2032000--2-22D3EE)

---

ZeroDep reads PDF files directly from bytes using **only the .NET Base Class Library** — no iText, no
Pdfium, no SkiaSharp, no native binaries, no transitive packages. It extracts the structure of a
document — image resolution, text and OCR layers, interactive forms, encryption metadata — and emits
a typed, versioned JSON graph. It is built for document-processing pipelines: ingestion, archive
validation, compliance checks, and large-corpus analysis.

The guiding principle is **"analyze, don't render."** Almost everything that forces a normal PDF
stack to pull in a native dependency is a *rendering* concern (rasterizing pages, decoding JPEG/JBIG2
pixels, hinting glyph outlines). By restricting itself to structural analysis, ZeroDep keeps the
zero-dependency promise while still answering the questions that matter for document workflows.

## Why ZeroDep

- **Zero supply-chain surface.** One managed assembly. No native assets, no RID-specific builds, no
  third-party CVEs to track. Trivial to vendor, audit, and deploy.
- **Broad reach.** Multi-targets `netstandard2.0` (so it runs on .NET Framework 4.6.1+) alongside
  modern `net8.0` and `net10.0`.
- **Deterministic and inspectable.** Pure managed code, stream-based and memory-conscious. The same
  bytes always produce the same typed result.
- **Safe by default.** Corrupted files are *rejected* with a structured reason rather than silently
  producing plausible-but-wrong data. Encryption and authentication failures are structured results,
  not crashes.

## Features

| Capability | What it does |
|------------|--------------|
| **Image DPI analysis** | Computes the effective resolution of every placed raster image from the content-stream CTM, reports the limiting (smaller) axis, and flags images below a configurable threshold. Page-area coverage is reported so small logos/stamps can be excluded from scan-quality judgments. |
| **JPEG decoding** | A complete pure-BCL JPEG (`/DCTDecode`) decoder — baseline, extended-sequential, progressive, and CMYK/YCCK (Adobe APP14) — decoding image streams to RGB/grayscale pixels and validating declared vs. actual dimensions. |
| **Text & OCR-layer extraction** | Pulls positioned text runs (`BT`/`ET`, `Tj`/`TJ`), resolves Unicode via `/ToUnicode` CMaps with `/Encoding` + `/Differences` fallback, and tags the invisible (`Tr 3`) OCR layer that scanners embed. |
| **AcroForm mapping** | Traverses `/AcroForm` fields by name (never geometry): fully-qualified names, labels, values, checkbox/radio state from the widget `/AS` + `/V`, page association, and dynamic-XFA detection. |
| **Encryption** | Standard security-handler decryption — RC4, AES-128 (AESV2), AES-256 (AESV3, R6) — authenticated with a supplied or empty/default password. |
| **Integrity validation** | A pre-flight gate rejects corrupted/malformed files with a machine-readable reason; it never attempts risky salvage. |
| **Typed JSON output** | The full parsed graph as a stable, versioned JSON schema, written by a pure-BCL serializer. |
| **Batch corpus runner** | A resumable, parallel runner over a directory tree that produces per-file results and publishable, content-free aggregate statistics. |
| **OCR-from-image** (opt-in) | A dependency-free `ZeroDep.Ocr` package with a pluggable `IOcrEngine`: decode a scanned page's image, run any OCR engine you supply, and fold the recovered text back in tagged `OcrGenerated` with confidence — never silently mixed with embedded text. |

## Scope: analyze, don't render

| ZeroDep does (structural / security) | ZeroDep does **not** do (rendering / out of scope) |
|--------------------------------------|----------------------------------------------------|
| Parse xref tables and streams, object streams, `/Prev` chains | Rasterize pages to bitmaps |
| Validate integrity and reject corrupted files | Salvage/repair broken files |
| Decrypt the standard security handler (RC4/AES-128/AES-256) | Public-key / certificate security handlers |
| Decode `/FlateDecode`, `/LZWDecode`, ASCII, and `/DCTDecode` (JPEG) to pixels | Decode `/CCITTFaxDecode`, `/JBIG2Decode` *pixels* (planned) |
| Read image `/Width`, `/Height`, `/BitsPerComponent` | Rasterize whole pages (vector + text + image compositing) |
| Interpret content-stream operators for text and placement | Execute path/shading/vector rendering |
| Resolve `/ToUnicode` to UTF-8 | Embed, subset, or rasterize fonts |
| Map `/AcroForm` fields and values | Render form appearances to pixels |
| Compute effective DPI from CTM math | Color management, ICC, blending |
| Read-only analysis | Edit, sign, or write PDFs back out |

## Install

```bash
dotnet add package ZeroDep
```

```xml
<PackageReference Include="ZeroDep" Version="1.0.0" />
```

## Quick start

The entry point is the static `PdfAnalyzer` in the `ZeroDep` namespace. Every method takes a readable
`Stream` and an optional password.

### Analyze a whole document

```csharp
using ZeroDep;
using ZeroDep.Abstractions;

using FileStream pdf = File.OpenRead("invoice.pdf");
DocumentAnalysis result = PdfAnalyzer.Analyze(pdf);

if (result.Status == DocumentStatus.Rejected)
{
    Console.WriteLine($"Rejected: {result.Rejection!.Reason}");
    return;
}

Console.WriteLine($"Pages: {result.PageCount}");
Console.WriteLine($"Encrypted: {result.Security.IsEncrypted} ({result.Security.Algorithm})");
Console.WriteLine($"Images: {result.Images.Count}, text runs: {result.TextRuns.Count}");
Console.WriteLine($"Form fields: {result.Form.Fields.Count}");
```

### Extract text (including the OCR layer)

```csharp
using FileStream pdf = File.OpenRead("scanned.pdf");

foreach (TextRunInfo run in PdfAnalyzer.ExtractText(pdf))
{
    string layer = run.IsOcrLayer ? "[ocr]" : "";
    Console.WriteLine($"p{run.PageIndex} ({run.X:F0},{run.Y:F0}) {layer} {run.Text}");
}

// or a simple line-grouped plain-text rendering:
string text = PdfAnalyzer.ExtractPlainText(File.OpenRead("scanned.pdf"));
```

### Flag low-resolution images

```csharp
using FileStream pdf = File.OpenRead("archive-scan.pdf");

foreach (ImageDpiInfo image in PdfAnalyzer.AnalyzeImageDpi(pdf, dpiThreshold: 150))
{
    if (image.IsBelowThreshold && image.PageAreaFraction >= 0.5)
    {
        Console.WriteLine($"Low-quality scan on page {image.PageIndex}: {image.EffectiveDpi:F0} DPI");
    }
}
```

### Read interactive form fields

```csharp
using FileStream pdf = File.OpenRead("sf1449.pdf");
AcroFormReport form = PdfAnalyzer.ExtractForm(pdf);

foreach (FormFieldInfo field in form.Fields)
{
    string value = field.IsChecked is bool b ? (b ? "[X]" : "[ ]") : field.Value ?? "";
    Console.WriteLine($"{field.FullyQualifiedName} = {value}");
}
```

### Open an encrypted document

```csharp
// Supplied password, or null for the empty/default user password.
DocumentAnalysis result = PdfAnalyzer.Analyze(File.OpenRead("secure.pdf"), password: "secret");
```

### Emit the JSON schema

```csharp
string json = PdfAnalyzer.ToJson(File.OpenRead("invoice.pdf"), indent: true);
File.WriteAllText("invoice.json", json);
```

### Decode a JPEG to pixels

```csharp
using ZeroDep.Filters;

byte[] jpeg = File.ReadAllBytes("photo.jpg");   // or a /DCTDecode image stream

JpegMetadata meta = JpegReader.ReadMetadata(jpeg);    // dimensions, mode, components — no pixel decode
Console.WriteLine($"{meta.Width}x{meta.Height}, {meta.Mode}");

RasterImage image = JpegDecoder.Decode(jpeg);   // baseline, progressive, or CMYK/YCCK → RGB/grayscale
// image.Samples is row-major, interleaved; image.Components is 1 (gray) or 3 (RGB)
```

### Recover text from a scanned page (OCR)

`ZeroDep.Ocr` is an opt-in, dependency-free package. You supply the OCR engine by implementing
`IOcrEngine` (over Tesseract, PaddleOCR, a cloud API — your choice); ZeroDep decodes the page image,
drives your engine, and merges the result as `OcrGenerated` text.

```csharp
using ZeroDep;
using ZeroDep.Abstractions;
using ZeroDep.Ocr;

sealed class MyEngine : IOcrEngine
{
    public OcrResult Recognize(DecodedImage image, OcrOptions options) => /* call your OCR engine */;
}

using var pdf = File.OpenRead("scanned.pdf");
DocumentAnalysis analysis = PdfAnalyzer.Analyze(pdf);

pdf.Position = 0;
IReadOnlyList<PdfImageInfo> images = PdfAnalyzer.ExtractImages(pdf);

DocumentAnalysis withOcr = OcrProcessor.Augment(analysis, images, new MyEngine(), new OcrOptions { MinConfidence = 0.6 });

foreach (TextRunInfo run in withOcr.TextRuns)
{
    if (run.Source == TextSource.OcrGenerated)
        Console.WriteLine($"[ocr {run.Confidence:P0}] {run.Text}");   // tagged, never mistaken for embedded text
}
```

### OCR accuracy — measured, not asserted

OCR ships against a measured accuracy benchmark, not on faith. The reference adapter was scored against
two independent labeled sets of real multilingual form documents — one spanning five European languages,
one an independent set of noisy low-resolution scans — using length-weighted Character Error Rate (CER)
after Unicode/whitespace/case normalization.

| Language | Character accuracy | Character accuracy at reported confidence ≥ 0.9 |
|----------|-------------------:|------------------------------------------------:|
| German | 95.8% | 99.2% |
| Spanish | 93.1% | 98.7% |
| Italian | 91.3% | 97.3% |
| Portuguese | 90.7% | 98.4% |
| French | 86.7% | 97.8% |
| English (noisy scans) | 83.8% | 95.0% |

The key property is **calibrated confidence**: on every set, error falls monotonically as the engine's
reported confidence rises — so at confidence ≥ 0.9 the reference adapter is **95–99% character-accurate**,
and lower-confidence output is honestly flagged rather than passed off as correct. Because confidence is
surfaced on every recovered run, you can threshold on it with trust.

Accuracy depends on the engine you plug in, which is the point of a pluggable `IOcrEngine`. The default
reference adapter targets Latin-script scans; for dense **CJK** (Chinese, Japanese) a second reference
adapter cuts the character error by **roughly half to two-thirds** on the same pages — so you pick the
engine that fits your documents without touching the dependency-free core. Both reference adapters reach
their engine out-of-process (a CLI binary / a worker process), so neither bundles native binaries.

## Command-line tool

A thin, zero-dependency console front-end ships alongside the library.

```bash
# Analyze one file (human-readable, or --json)
zerodep document.pdf
zerodep document.pdf --json --password=secret

# Batch-process a directory tree (recursive)
zerodep batch ./corpus --output=./batch-out
```

The `batch` verb is **resumable** and **parallel**. It writes three things under the output folder:
a resumable ledger, optional per-file JSON (`--no-perfile` to skip), and a publishable
`aggregate.json` containing **aggregates and counts only** — no file names, extracted text, or field
values. Useful flags: `--concurrency=N`, `--threshold=N`, `--no-resume`, `--no-perfile`.

### Batch from code

```csharp
using ZeroDep.Batch;

var summary = await new BatchProcessor().RunAsync(new BatchOptions
{
    InputDirectory = "./corpus",
    OutputDirectory = "./batch-out",
});

Console.WriteLine($"Processed {summary.Total}, rejected {summary.Rejected}");
foreach (var category in summary.Statistics.Categories)
{
    Console.WriteLine($"  {category.Category}: {category.Count} ({category.Percent}%)");
}
```

Each document is classified into a content-free, structural category — `DigitalText`,
`ScannedImageOnly`, `ScannedWithOcr`, `FormBased`, `Mixed`, `EncryptedUnreadable`, or `Rejected` —
derived only from how the file is built, never from what it says.

## Supported frameworks

| Target | Use |
|--------|-----|
| `netstandard2.0` | .NET Framework 4.6.1+, older runtimes |
| `net8.0` | Modern .NET (LTS) |
| `net10.0` | Latest .NET |

Crypto uses the in-box `System.Security.Cryptography` (AES, MD5, SHA-2 ship with every target); RC4
is implemented in-house. Building from source requires the .NET SDK **10.0.300+**.

## Output schema (overview)

`PdfAnalyzer.ToJson` emits a stable, versioned envelope. Top-level `status` lets a pipeline branch in
a single read; every page-level array is always present (possibly empty); undecodable assets are
reported with flags rather than dropped silently.

```jsonc
{
  "schemaVersion": "1.0",
  "status": "Processed",            // Processed | Rejected
  "rejection": null,                // { "reason": "TruncatedStream", "detail": "..." } when Rejected
  "pageCount": 3,
  "imageAreaFraction": 0.94,
  "security": { "isEncrypted": true, "algorithm": "AES-256", "authentication": "UserPassword", "...": "..." },
  "images":   [ { "page": 0, "effectiveDpi": 200.0, "belowThreshold": false, "pageAreaFraction": 0.94 } ],
  "textRuns": [ { "page": 0, "text": "Invoice", "renderMode": 0, "isOcrLayer": false } ],
  "form":     { "hasAcroForm": true, "hasXfa": false, "fields": [ /* ... */ ] }
}
```

## Architecture

ZeroDep is a layered pipeline; each layer depends only on the layers below it:

```
bytes → lexer → object resolver → security/decryption → filters
      → document model → content interpreter → feature engines → typed result → JSON
```

| Layer | Responsibility |
|-------|----------------|
| Byte source | Seekable, buffered windows over the stream; no whole-file load |
| Lexer | Names, numbers, strings, dictionaries, arrays |
| Integrity validator | Pre-flight accept/reject gate (no salvage) |
| Object resolver | Xref tables + streams, object streams, indirect refs, `/Prev` chains |
| Security | Standard-handler key derivation and per-object decryption |
| Filters | Flate, LZW, ASCIIHex/85, predictors; full JPEG (`/DCTDecode`) decode; metadata-only passthrough for CCITT/JBIG2 |
| Document model | Catalog, page tree, resources, fonts, AcroForm |
| Content interpreter | Text and image-placement operator state machine |
| Feature engines | DPI, text/OCR, forms |
| Serialization | Typed graph → versioned JSON (pure BCL) |

## Roadmap

Each release is **additive** and keeps the shipped public API backward-compatible. The core engine
stays 100% dependency-free — only the OCR stage introduces an opt-in dependency, isolated in its own
package.

| Release | Capability | Status |
|---------|------------|--------|
| **1.0.x** | Structural analysis: DPI, text & OCR-layer, AcroForms, encryption, validation, JSON, batch | Released |
| **1.1.0** | **JPEG (`/DCTDecode`) pure-BCL decode** — baseline, extended-sequential, progressive, and CMYK/YCCK. Gives OCR real pixels and validates declared vs. actual image dimensions | Released |
| **1.2.0** (current) | **Text from images (OCR)** — opt-in, dependency-free `ZeroDep.Ocr` with a pluggable `IOcrEngine`; recovers text from raster pages with no embedded text layer (`ScannedImageOnly` → `ScannedWithOcr`) | Released |
| **1.2.1** | Reference engine adapters — `ZeroDep.Ocr.Tesseract` (Latin scripts) and `ZeroDep.Ocr.Paddle` (CJK), both out-of-process and dependency-free, gated on a measured accuracy benchmark | In progress |
| 1.3.0 | CCITT Group 3/4 + RunLength decoders (bi-level scans → OCR coverage) | Planned |
| 1.4.0 | JBIG2 and JPX (JPEG 2000) decoders | Planned |
| 1.5.0 | Color pipeline (DeviceRGB/Gray/CMYK, Indexed, ICC) | Planned |
| 2.0.0 | Font program parsing & glyph rasterization | Planned |
| 2.1.0 | Full page rendering | Planned |

## Contributing

Issues and pull requests are welcome. Please run `dotnet test ZeroDep.slnx -c Release` before
submitting; all targets must build clean (warnings are treated as errors).

## License

Licensed under the [Apache License 2.0](https://github.com/Nerttiyana-Technologies/ZeroDep/blob/main/LICENSE).
