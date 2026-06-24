using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using ZeroDep.Abstractions;

namespace ZeroDep.Ocr.Paddle;

/// <summary>
/// An <see cref="IOcrEngine"/> that recovers text from images by driving the PaddleOCR Python package
/// through a bundled bridge script (<c>zerodep_paddle_ocr.py</c>) as a subprocess, parsing its JSON
/// output for per-line text, confidence, and bounding boxes. PaddleOCR is notably stronger than
/// Tesseract on dense and CJK scripts.
/// </summary>
/// <remarks>
/// <para>
/// This adapter carries <b>no native or managed package dependency</b>: it shells out to a Python
/// interpreter that has the <c>paddleocr</c> package installed (<c>pip install paddleocr</c>). That
/// keeps platform support equal to PaddleOCR's own — including Apple-silicon macOS — and avoids binding
/// native PaddleInference in-process.
/// </para>
/// <para>
/// Each <see cref="Recognize"/> call spawns its own short-lived process, so instances are safe to use
/// concurrently. <see cref="Dispose"/> is a no-op, retained for interface symmetry.
/// </para>
/// </remarks>
public sealed class PaddleOcrEngine : IOcrEngine, IDisposable
{
    private readonly string _python;
    private readonly string _script;
    private readonly string _language;
    private readonly object _gate = new object();
    private readonly Queue<string> _stderrTail = new Queue<string>();
    private Process? _worker;
    private bool _disposed;

    /// <summary>Creates an engine that drives PaddleOCR via the bundled Python bridge.</summary>
    /// <param name="language">A PaddleOCR language code, e.g. <c>en</c>, <c>fr</c>, <c>german</c>, <c>ch</c>, <c>japan</c>.</param>
    /// <param name="pythonExecutable">The Python interpreter to run; defaults to <c>python3</c> on <c>PATH</c>.</param>
    /// <param name="scriptPath">
    /// Path to <c>zerodep_paddle_ocr.py</c>; defaults to the copy shipped next to this assembly.
    /// </param>
    public PaddleOcrEngine(string language = "en", string pythonExecutable = "python3", string? scriptPath = null)
    {
        _language = string.IsNullOrWhiteSpace(language) ? "en" : language;
        _python = string.IsNullOrWhiteSpace(pythonExecutable) ? "python3" : pythonExecutable;
        _script = scriptPath ?? Path.Combine(AppContext.BaseDirectory, "zerodep_paddle_ocr.py");
    }

    /// <inheritdoc/>
    public OcrResult Recognize(DecodedImage image, OcrOptions options)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        options ??= new OcrOptions();
        var lines = new List<OcrLine>();
        BoundingBox pageBounds = new BoundingBox(0, 0, image.Width, image.Height);

        foreach (PaddleWord word in RunPaddle(image))
        {
            if (word.Confidence < options.MinConfidence)
            {
                continue;
            }

            lines.Add(new OcrLine
            {
                Text = word.Text,
                Confidence = word.Confidence,
                Bounds = word.Box ?? pageBounds,
            });
        }

        return new OcrResult { Lines = lines };
    }

    /// <summary>
    /// Recognizes detections with their bounding boxes and confidences. Used by the accuracy benchmark
    /// for box-aligned scoring. Not part of the public OCR surface.
    /// </summary>
    internal IReadOnlyList<OcrWordBox> RecognizeWords(DecodedImage image)
    {
        if (image is null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        var words = new List<OcrWordBox>();
        foreach (PaddleWord word in RunPaddle(image))
        {
            words.Add(new OcrWordBox(
                word.Text,
                word.Confidence,
                word.Box ?? new BoundingBox(0, 0, image.Width, image.Height)));
        }

        return words;
    }

    /// <summary>Shuts down the persistent PaddleOCR worker process, if one was started.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_worker is not null)
            {
                try
                {
                    if (!_worker.HasExited)
                    {
                        _worker.StandardInput.Close();   // EOF → the worker's read loop exits
                        if (!_worker.WaitForExit(3000))
                        {
                            _worker.Kill();
                        }
                    }
                }
                catch
                {
                    // Best-effort shutdown.
                }

                _worker.Dispose();
                _worker = null;
            }
        }
    }

    private IReadOnlyList<PaddleWord> RunPaddle(DecodedImage image)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "zerodep-ocr-" + Guid.NewGuid().ToString("N") + ".bmp");
        try
        {
            File.WriteAllBytes(tmp, Bmp.Encode(image));

            lock (_gate)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(PaddleOcrEngine));
                }

                Process worker = EnsureWorker();
                worker.StandardInput.WriteLine(tmp);
                worker.StandardInput.Flush();

                string? line = worker.StandardOutput.ReadLine();
                if (line is null)
                {
                    throw new InvalidOperationException(
                        $"paddleocr worker exited unexpectedly. Recent stderr:\n{StderrTail()}");
                }

                return ParseJson(line);
            }
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

    // Starts (once) the persistent worker and waits for its READY handshake. Caller holds _gate.
    private Process EnsureWorker()
    {
        if (_worker is not null && !_worker.HasExited)
        {
            return _worker;
        }

        if (!File.Exists(_script))
        {
            throw new InvalidOperationException(
                $"PaddleOCR bridge script not found at '{_script}'. Ensure zerodep_paddle_ocr.py ships with the adapter.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = _python,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add(_script);
        psi.ArgumentList.Add("--serve");
        psi.ArgumentList.Add(_language);

        var proc = new Process { StartInfo = psi };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (_stderrTail)
            {
                _stderrTail.Enqueue(e.Data);
                while (_stderrTail.Count > 50)
                {
                    _stderrTail.Dequeue();
                }
            }
        };

        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not start '{_python}'. Install Python and the paddleocr package (pip install paddleocr).", ex);
        }

        proc.BeginErrorReadLine();

        // Block until the worker has loaded its model (first run may download models).
        string? ready = proc.StandardOutput.ReadLine();
        if (ready is null || !ready.StartsWith("READY", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"paddleocr worker failed to initialize. Recent stderr:\n{StderrTail()}");
        }

        _worker = proc;
        return _worker;
    }

    private string StderrTail()
    {
        lock (_stderrTail)
        {
            return string.Join("\n", _stderrTail);
        }
    }

    private static List<PaddleWord> ParseJson(string json)
    {
        var words = new List<PaddleWord>();
        string trimmed = json.Trim();
        if (trimmed.Length == 0)
        {
            return words;
        }

        using JsonDocument doc = JsonDocument.Parse(trimmed);
        if (!doc.RootElement.TryGetProperty("words", out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return words;
        }

        foreach (JsonElement w in arr.EnumerateArray())
        {
            string text = w.TryGetProperty("text", out JsonElement t) ? (t.GetString() ?? string.Empty) : string.Empty;
            if (text.Length == 0)
            {
                continue;
            }

            double conf = w.TryGetProperty("conf", out JsonElement c) ? c.GetDouble() : 0.0;
            BoundingBox? box = null;
            if (w.TryGetProperty("box", out JsonElement b) && b.ValueKind == JsonValueKind.Array && b.GetArrayLength() >= 4)
            {
                box = new BoundingBox(b[0].GetDouble(), b[1].GetDouble(), b[2].GetDouble(), b[3].GetDouble());
            }

            words.Add(new PaddleWord(text, conf, box));
        }

        return words;
    }

    private readonly struct PaddleWord
    {
        public PaddleWord(string text, double confidence, BoundingBox? box)
        {
            Text = text;
            Confidence = confidence;
            Box = box;
        }

        public string Text { get; }

        public double Confidence { get; }

        public BoundingBox? Box { get; }
    }
}

/// <summary>A single recognized detection with its confidence and image-space bounding box.</summary>
internal sealed class OcrWordBox
{
    public OcrWordBox(string text, double confidence, BoundingBox box)
    {
        Text = text;
        Confidence = confidence;
        Box = box;
    }

    /// <summary>The recognized text.</summary>
    public string Text { get; }

    /// <summary>Confidence in [0, 1].</summary>
    public double Confidence { get; }

    /// <summary>Bounding box in image pixels (origin top-left).</summary>
    public BoundingBox Box { get; }
}
