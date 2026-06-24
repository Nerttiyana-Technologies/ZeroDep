# ZeroDep.Ocr.Tesseract

A [Tesseract](https://github.com/tesseract-ocr/tesseract) `IOcrEngine` adapter for
[`ZeroDep.Ocr`](https://www.nuget.org/packages/ZeroDep.Ocr). It recovers text from scanned pages that
have no embedded text layer. This package carries an **opt-in native dependency** (libtesseract); the
ZeroDep core itself stays dependency-free.

> Wraps the `Tesseract` (charlesw) package. The maintained fork `TesseractOCR` (Sicos1977) is a drop-in
> alternative — the `IOcrEngine` surface is tiny, so swapping the wrapper is a few lines.

## Setup

1. **Install the native Tesseract engine** (provides `libtesseract` / `libleptonica`):
   - macOS: `brew install tesseract`
   - Ubuntu/Debian: `sudo apt-get install -y tesseract-ocr libtesseract-dev libleptonica-dev`
   - Windows: the `Tesseract` NuGet package ships the native binaries.
2. **Get the language data** (`.traineddata`) into a `tessdata` directory — download from
   [tessdata_fast](https://github.com/tesseract-ocr/tessdata_fast) (e.g. `eng.traineddata`,
   `deu.traineddata`, `chi_sim.traineddata`).

## Usage

```csharp
using ZeroDep;
using ZeroDep.Abstractions;
using ZeroDep.Ocr;
using ZeroDep.Ocr.Tesseract;

using var engine = new TesseractOcrEngine(tessDataPath: "./tessdata", languages: "eng+deu");

using var pdf = File.OpenRead("scanned.pdf");
DocumentAnalysis analysis = PdfAnalyzer.Analyze(pdf);
pdf.Position = 0;
var images = PdfAnalyzer.ExtractImages(pdf);

DocumentAnalysis withOcr = OcrProcessor.Augment(analysis, images, engine, new OcrOptions { MinConfidence = 0.6 });

foreach (var run in withOcr.TextRuns)
    if (run.Source == TextSource.OcrGenerated)
        Console.WriteLine($"[{run.Confidence:P0}] {run.Text}");
```

## Notes

- `TesseractEngine` is **not thread-safe**; this adapter serializes calls internally. For throughput,
  create one engine per worker.
- The decoded image is wrapped in an in-memory BMP before recognition (no temp files).
- Confidence is reported per line (0–1); set `OcrOptions.MinConfidence` to drop low-confidence lines.
