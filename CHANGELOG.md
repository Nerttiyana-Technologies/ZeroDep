# Changelog

All notable changes to **ZeroDep** are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 1.2.0 — Unreleased

### Added

- **OCR-from-image (`ZeroDep.Ocr`).** A new opt-in, **dependency-free** package that recovers text
  from scanned pages with no embedded text layer. It defines a pluggable `IOcrEngine` (bring your own
  engine — Tesseract, PaddleOCR, a cloud API), decodes the page's image via the 1.1.0 JPEG decoder,
  and folds recovered lines into the analysis as `OcrGenerated` runs with confidence — never silently
  merged with embedded text. Public API: `IOcrEngine`, `DecodedImage`, `OcrOptions`, `OcrResult`,
  `OcrLine`, `OcrImageConverter`, `OcrProcessor`.
- **`PdfAnalyzer.ExtractImages`** — enumerate a document's embedded image XObjects (page, declared
  size, filter, raw bytes) via the new public `PdfImageInfo`.
- **`TextRunInfo.Source`** (`Embedded` | `OcrGenerated`) and **`TextRunInfo.Confidence`** — text
  provenance, so OCR output is always distinguishable from embedded text. A page that gains OCR text
  reclassifies `ScannedImageOnly` → `ScannedWithOcr` (OCR text is not counted as embedded text).

### Notes

- The OCR core ships with **no bundled engine** and adds no dependency to any ZeroDep package. A
  reference engine adapter (Tesseract / PaddleOCR) will ship separately (1.2.x), gated on a measured
  accuracy benchmark (CER/WER on a labeled ground-truth set).

## 1.1.0

### Added

- **Pure-BCL JPEG (`/DCTDecode`) decoder.** A complete, dependency-free JPEG decoder covering
  **baseline**, **extended-sequential**, and **progressive** coding, plus **CMYK/YCCK** (Adobe APP14,
  inverted-CMYK handling), chroma upsampling, restart intervals, and YCbCr→RGB conversion. New public
  types in `ZeroDep.Filters`: `JpegReader` (header/dimension parsing and validation), `JpegDecoder`
  (full pixel decode to `RasterImage`), `JpegMetadata`, `JpegMode`, and `JpegComponent`. Validated
  against 105,000+ real embedded JPEGs (baseline, progressive, and CMYK) with zero decode errors; the
  header parser also flags non-conformant PDFs whose declared image dimensions disagree with the JPEG.

### Notes

- This decoder provides the pixels for the upcoming OCR stage (1.2.0) and lets callers validate
  declared vs. actual image dimensions. The core engine remains 100% dependency-free.

## 1.0.1

### Fixed

- Documentation: the NuGet package README now uses plain Markdown with an absolute header-image URL,
  so it renders correctly on nuget.org. The previous centered raw-HTML block and relative image path
  did not render on NuGet (they appeared as literal text).

## 1.0.0

The first public release of ZeroDep: a 100% dependency-free .NET engine for PDF **structural
analysis**. It reads PDF files directly from bytes using only the .NET Base Class Library — no
third-party or native libraries — and emits a typed, versioned JSON graph.

### Added

**Core pipeline (pure BCL)**

- Byte source, lexer, and object resolver: classic xref tables, cross-reference streams, object
  streams, indirect references, and `/Prev` incremental-update chains.
- Filters: `/FlateDecode` (with PNG/TIFF predictors), `/LZWDecode`, `/ASCIIHexDecode`,
  `/ASCII85Decode`, `/RunLengthDecode`; metadata-only passthrough for `/DCTDecode`,
  `/CCITTFaxDecode`, and `/JBIG2Decode` (dimensions read, pixels never decoded).
- Document model: catalog and page-tree traversal with inherited `/MediaBox`, `/Resources`, and
  `/Rotate`.
- Content interpreter: a text and image-placement operator state machine (`BT`/`ET`, `Tj`/`TJ`,
  `cm`, `Tr`, `Tf`, `BI`/`ID`/`EI`).

**Features**

- **Image DPI analysis** — effective resolution per placed image from CTM math, reported on the
  limiting (smaller) axis and flagged against a configurable threshold. Per-image page-area coverage
  is reported so small logos/stamps can be excluded from scan-quality judgments.
- **Text & OCR-layer extraction** — positioned text runs with `/ToUnicode` decoding, `/Encoding`
  and `/Differences` fallback, and tagging of the invisible (`Tr 3`) OCR layer.
- **AcroForm mapping** — fully-qualified field names via the `/Parent` chain, labels, values,
  checkbox/radio state from the widget `/AS` + field `/V`, page association, and dynamic-XFA
  detection.
- **Encryption** — standard security-handler decryption: RC4, AES-128 (AESV2), and AES-256 (AESV3,
  revision 6), authenticated with a supplied or empty/default password.
- **Integrity validation** — a pre-flight gate that rejects corrupted/malformed files with a
  machine-readable reason (`MissingHeader`, `MissingEof`, `XrefUnresolvable`, `CatalogUnreachable`,
  `TruncatedStream`, `MalformedObject`, `EncryptionUnsupported`, `EncryptedPasswordRequired`). No
  salvage is attempted.
- **Typed JSON output** — the parsed graph as a stable, versioned schema via a pure-BCL serializer.

**Batch corpus runner**

- A resumable, parallel runner (`ZeroDep.Batch`) over a directory tree, with bounded concurrency,
  failure isolation (corruption and authentication failure are structured results, not exceptions),
  and order-independent aggregation.
- A content-free structural classifier (`DigitalText`, `ScannedImageOnly`, `ScannedWithOcr`,
  `FormBased`, `Mixed`, `EncryptedUnreadable`, `Rejected`) derived only from how a document is built.
- Publishable aggregate statistics containing **aggregates and counts only** — by construction the
  type carries no file names, extracted text, or field values.

**Tooling**

- `ZeroDep.Cli` — a zero-dependency console front-end for single-file analysis (`--json`,
  `--password`) and recursive batch processing (`batch`, with `--output`, `--concurrency`,
  `--threshold`, `--no-resume`, `--no-perfile`).

### Behavior notes

- **Analyze, don't render.** ZeroDep performs read-only structural analysis. It does not rasterize
  pages, decode image pixels, rasterize fonts, or write PDFs back out.
- **Reject, don't repair.** Corrupted files are rejected with a structured reason rather than
  salvaged, so a pipeline never receives plausible-but-wrong data from a damaged document.
- **No-throw for expected failures.** Corruption and authentication failure are returned as
  structured results; only genuinely exceptional conditions throw.

### Supported frameworks

`netstandard2.0` (for .NET Framework 4.6.1+), `net8.0`, and `net10.0`. Built with the .NET SDK
10.0.300+.

### Known limitations

- Public-key (certificate) security handlers are detected and reported as unsupported, not decrypted.
- Documents that require a password ZeroDep is not given are rejected with `EncryptedPasswordRequired`.
- Fonts without a usable `/ToUnicode` map (e.g. some subset CID fonts) yield partial or flagged text
  by design — undecodable runs are emitted empty rather than as control-byte garbage.

### Roadmap

Each release is additive and keeps the public API backward-compatible; the core stays 100%
dependency-free.

- **1.1.0 (next)** — JPEG (`/DCTDecode`) pure-BCL decode: the prerequisite that gives OCR real pixels.
- **1.2.0 (highest priority)** — text from images (OCR): an opt-in `ZeroDep.Ocr` package + `IOcrEngine`
  adapters that read text from raster pages with no embedded text layer (today `ScannedImageOnly`).
- **1.3.0** — CCITT Group 3/4 + RunLength decoders (bi-level scans → OCR coverage).
- **1.4.0** — JBIG2 and JPX (JPEG 2000) decoders.
- **1.5.0** — color pipeline (DeviceRGB/Gray/CMYK, Indexed, ICC).
- **2.0.0** — font program parsing & glyph rasterization.
- **2.1.0** — full page rendering.
