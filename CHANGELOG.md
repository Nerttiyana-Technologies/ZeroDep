# Changelog

All notable changes to **ZeroDep** are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 1.0.1 — Unreleased

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

- **1.1.0 (next, highest priority)** — text from images (OCR): an opt-in `ZeroDep.Ocr` package that
  reads text from raster pages with no embedded text layer (today classified `ScannedImageOnly`).
- **1.2.0** — JPEG (`/DCTDecode`) pixel decode.
- **1.3.0** — CCITT Group 3/4, LZW-image, and RunLength decoders.
- **1.4.0** — JBIG2 and JPX (JPEG 2000) decoders.
- **1.5.0** — color pipeline (DeviceRGB/Gray/CMYK, Indexed, ICC).
- **2.0.0** — font program parsing & glyph rasterization.
- **2.1.0** — full page rendering.
