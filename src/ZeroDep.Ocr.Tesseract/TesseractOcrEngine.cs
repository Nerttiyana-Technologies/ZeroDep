using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using ZeroDep.Abstractions;

namespace ZeroDep.Ocr.Tesseract;

/// <summary>
/// An <see cref="IOcrEngine"/> that recovers text from images by invoking the <c>tesseract</c>
/// command-line program as a subprocess and parsing its TSV output for per-line text, confidence,
/// and bounding boxes.
/// </summary>
/// <remarks>
/// <para>
/// This adapter carries <b>no native or managed package dependency</b>: it shells out to a
/// <c>tesseract</c> executable that the host already has on its <c>PATH</c> (e.g. <c>brew install
/// tesseract</c>, <c>apt-get install tesseract-ocr</c>, or the Windows installer). That keeps
/// platform support equal to Tesseract's own — including Apple-silicon macOS — and avoids the
/// in-process P/Invoke loaders that ship only x64 binaries.
/// </para>
/// <para>
/// Each <see cref="Recognize"/> call spawns its own short-lived process, so instances are safe to
/// use concurrently. <see cref="Dispose"/> is a no-op, retained for interface symmetry.
/// </para>
/// </remarks>
public sealed class TesseractOcrEngine : IOcrEngine, IDisposable
{
    private readonly string _exe;
    private readonly string? _tessDataDir;
    private readonly string _languages;

    /// <summary>Creates an engine that drives the <c>tesseract</c> CLI.</summary>
    /// <param name="tessDataPath">
    /// Path to the <c>tessdata</c> directory holding the <c>.traineddata</c> models, passed as
    /// <c>--tessdata-dir</c>. Pass <see langword="null"/> to use Tesseract's default lookup
    /// (<c>TESSDATA_PREFIX</c> / install location).
    /// </param>
    /// <param name="languages">Language code(s), <c>+</c>-joined, e.g. <c>eng</c> or <c>eng+fra</c>.</param>
    /// <param name="executable">The <c>tesseract</c> program to run; defaults to <c>tesseract</c> on <c>PATH</c>.</param>
    public TesseractOcrEngine(string? tessDataPath, string languages = "eng", string executable = "tesseract")
    {
        _tessDataDir = string.IsNullOrWhiteSpace(tessDataPath) ? null : tessDataPath;
        _languages = string.IsNullOrWhiteSpace(languages) ? "eng" : languages;
        _exe = string.IsNullOrWhiteSpace(executable) ? "tesseract" : executable;
    }

    /// <inheritdoc/>
    public OcrResult Recognize(DecodedImage image, OcrOptions options)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        options ??= new OcrOptions();

        string tmp = Path.Combine(Path.GetTempPath(), "zerodep-ocr-" + Guid.NewGuid().ToString("N") + ".bmp");
        try
        {
            File.WriteAllBytes(tmp, Bmp.Encode(image));
            string tsv = RunTesseract(tmp);
            return new OcrResult { Lines = ParseTsv(tsv, options, image) };
        }
        finally
        {
            try
            {
                File.Delete(tmp);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a leftover temp file must not fail recognition.
            }
        }
    }

    /// <summary>
    /// Recognizes individual words with their bounding boxes and confidences. Used by the accuracy
    /// benchmark to score OCR against datasets that annotate text by region (e.g. XFUND), eliminating
    /// reading-order ambiguity. Not part of the public OCR surface.
    /// </summary>
    internal IReadOnlyList<OcrWordBox> RecognizeWords(DecodedImage image)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        string tmp = Path.Combine(Path.GetTempPath(), "zerodep-ocr-" + Guid.NewGuid().ToString("N") + ".bmp");
        try
        {
            File.WriteAllBytes(tmp, Bmp.Encode(image));
            return ParseWords(RunTesseract(tmp));
        }
        finally
        {
            try
            {
                File.Delete(tmp);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>No-op; the CLI adapter holds no unmanaged resources.</summary>
    public void Dispose()
    {
    }

    // Level-5 TSV rows are words: left top width height conf text.
    private static List<OcrWordBox> ParseWords(string tsv)
    {
        var words = new List<OcrWordBox>();
        foreach (string raw in tsv.Split('\n'))
        {
            string row = raw.TrimEnd('\r');
            if (row.Length == 0)
            {
                continue;
            }

            string[] f = row.Split('\t');
            if (f.Length < 12 || f[0] != "5")
            {
                continue;
            }

            string text = f[11];
            double conf = ParseDouble(f[10]);
            if (text.Length == 0 || conf < 0)
            {
                continue;
            }

            words.Add(new OcrWordBox(
                text,
                conf / 100.0,
                new BoundingBox(ParseInt(f[6]), ParseInt(f[7]), ParseInt(f[8]), ParseInt(f[9]))));
        }

        return words;
    }

    private string RunTesseract(string imagePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // tesseract <image> stdout [--tessdata-dir DIR] -l LANGS --psm 3 tsv
        psi.ArgumentList.Add(imagePath);
        psi.ArgumentList.Add("stdout");
        if (_tessDataDir is not null)
        {
            psi.ArgumentList.Add("--tessdata-dir");
            psi.ArgumentList.Add(_tessDataDir);
        }

        psi.ArgumentList.Add("-l");
        psi.ArgumentList.Add(_languages);
        psi.ArgumentList.Add("--psm");
        psi.ArgumentList.Add("3");

        // Enable the TSV renderer by variable rather than the `tsv` config-file name: the config
        // files live in the install's tessdata, which --tessdata-dir overrides away from.
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("tessedit_create_tsv=1");

        using var proc = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) { stdout.AppendLine(e.Data); } };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) { stderr.AppendLine(e.Data); } };

        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not start '{_exe}'. Install Tesseract and ensure it is on PATH (e.g. 'brew install tesseract').", ex);
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"tesseract exited with code {proc.ExitCode}: {stderr.ToString().Trim()}");
        }

        return stdout.ToString();
    }

    // TSV columns: level page block par line word left top width height conf text
    private static List<OcrLine> ParseTsv(string tsv, OcrOptions options, DecodedImage image)
    {
        var lines = new List<OcrLine>();
        var words = new List<string>();
        var confs = new List<double>();
        BoundingBox bounds = new BoundingBox(0, 0, image.Width, image.Height);
        bool open = false;

        void Flush()
        {
            if (!open || words.Count == 0)
            {
                open = false;
                words.Clear();
                confs.Clear();
                return;
            }

            double confidence = 0;
            foreach (double c in confs)
            {
                confidence += c;
            }

            confidence = confidence / confs.Count / 100.0;

            if (confidence >= options.MinConfidence)
            {
                lines.Add(new OcrLine
                {
                    Text = string.Join(" ", words),
                    Confidence = confidence,
                    Bounds = bounds,
                });
            }

            open = false;
            words.Clear();
            confs.Clear();
        }

        foreach (string raw in tsv.Split('\n'))
        {
            string row = raw.TrimEnd('\r');
            if (row.Length == 0)
            {
                continue;
            }

            string[] f = row.Split('\t');
            if (f.Length < 12 || (f[0] != "4" && f[0] != "5"))
            {
                continue; // header row, or block/par/page levels
            }

            if (f[0] == "4") // a new text line
            {
                Flush();
                open = true;
                bounds = new BoundingBox(
                    ParseInt(f[6]), ParseInt(f[7]), ParseInt(f[8]), ParseInt(f[9]));
            }
            else if (open) // f[0] == "5": a word within the current line
            {
                string text = f[11];
                if (text.Length == 0)
                {
                    continue;
                }

                words.Add(text);
                confs.Add(ParseDouble(f[10]));
            }
        }

        Flush();
        return lines;
    }

    private static int ParseInt(string s)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    private static double ParseDouble(string s)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
}

/// <summary>A single recognized word with its confidence and image-space bounding box.</summary>
internal sealed class OcrWordBox
{
    public OcrWordBox(string text, double confidence, BoundingBox box)
    {
        Text = text;
        Confidence = confidence;
        Box = box;
    }

    /// <summary>The recognized word text.</summary>
    public string Text { get; }

    /// <summary>Confidence in [0, 1].</summary>
    public double Confidence { get; }

    /// <summary>Word bounding box in image pixels (origin top-left).</summary>
    public BoundingBox Box { get; }
}
