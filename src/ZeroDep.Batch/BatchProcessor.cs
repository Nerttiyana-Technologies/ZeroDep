using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZeroDep;
using ZeroDep.Abstractions;
using ZeroDep.Json;

namespace ZeroDep.Batch;

/// <summary>
/// Resumable, parallel corpus runner (ADR Feature G). It produces three outputs under the output
/// directory: per-file verification JSON (internal, keyed by anonymized id), a resumable ledger, and
/// the publishable aggregate statistics (Feature H / §8.2). One bad file never aborts the run —
/// corruption and authentication failure are structured results, not exceptions.
/// </summary>
public sealed class BatchProcessor
{
    private const string LedgerFileName = "ledger.tsv";
    private const string AggregateFileName = "aggregate.json";
    private const string PerFileDirectoryName = "perfile";

    private readonly object _ledgerLock = new object();

    /// <summary>Runs the corpus batch described by <paramref name="options"/>.</summary>
    /// <param name="options">The batch configuration.</param>
    /// <param name="cancellationToken">A token to cancel the run.</param>
    /// <returns>A summary of the run, including the computed aggregate statistics.</returns>
    public async Task<BatchSummary> RunAsync(BatchOptions options, CancellationToken cancellationToken = default)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        Directory.CreateDirectory(options.OutputDirectory);
        string perFileRoot = Path.Combine(options.OutputDirectory, PerFileDirectoryName);
        if (options.WritePerFileJson)
        {
            Directory.CreateDirectory(perFileRoot);
        }

        string ledgerPath = Path.Combine(options.OutputDirectory, LedgerFileName);
        Dictionary<string, FileOutcome> priorLedger = LoadLedger(ledgerPath);

        string[] files = Directory.GetFiles(options.InputDirectory, "*.pdf", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);

        var outcomes = new ConcurrentBag<FileOutcome>();
        int[] counters = new int[2]; // [0] = processed this run, [1] = skipped

        using var ledgerWriter = new StreamWriter(
            new FileStream(ledgerPath, FileMode.Append, FileAccess.Write, FileShare.Read));

        void ProcessOne(string path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string absolute = Path.GetFullPath(path);
            string id = BatchId.ForPath(absolute);
            string contentHash = BatchId.ForContent(path);

            if (options.Resume
                && priorLedger.TryGetValue(id, out FileOutcome? prior)
                && prior.InputHash == contentHash)
            {
                outcomes.Add(prior);
                Interlocked.Increment(ref counters[1]);
                return;
            }

            FileOutcome outcome = ProcessFile(path, id, contentHash, options, perFileRoot);
            outcomes.Add(outcome);
            Interlocked.Increment(ref counters[0]);

            string line = outcome.ToLedgerLine();
            lock (_ledgerLock)
            {
                ledgerWriter.WriteLine(line);
                ledgerWriter.Flush();
            }
        }

        int degree = Math.Max(1, options.MaxConcurrency);
#if NET8_0_OR_GREATER
        await Parallel.ForEachAsync(
            files,
            new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = cancellationToken },
            (path, _) =>
            {
                ProcessOne(path);
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);
#else
        using (var gate = new SemaphoreSlim(degree))
        {
            var tasks = new List<Task>();
            foreach (string path in files)
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                tasks.Add(Task.Run(
                    () =>
                    {
                        try
                        {
                            ProcessOne(path);
                        }
                        finally
                        {
                            gate.Release();
                        }
                    },
                    cancellationToken));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
#endif

        List<FileOutcome> all = outcomes.ToList();
        AggregateStatistics statistics = Aggregator.Build(all, options.Thresholds);
        string aggregatePath = Path.Combine(options.OutputDirectory, AggregateFileName);
        File.WriteAllText(aggregatePath, AggregateStatisticsJson.Write(statistics, indent: true));

        return new BatchSummary
        {
            Total = files.Length,
            ProcessedThisRun = counters[0],
            Skipped = counters[1],
            Rejected = all.Count(o => o.Status == DocumentStatus.Rejected),
            EncryptedUnreadable = all.Count(o => o.EncryptedUnreadable),
            AggregatePath = aggregatePath,
            Statistics = statistics,
        };
    }

    private static FileOutcome ProcessFile(string path, string id, string contentHash, BatchOptions options, string perFileRoot)
    {
        DocumentAnalysis analysis;
        try
        {
            using FileStream stream = File.OpenRead(path);
            analysis = PdfAnalyzer.Analyze(stream, options.DpiThreshold, options.Password);
        }
        catch (Exception)
        {
            // An unexpected failure is isolated to this file: record a rejection, never abort the run.
            return new FileOutcome
            {
                Id = id,
                InputHash = contentHash,
                Status = DocumentStatus.Rejected,
                Category = DocumentCategory.Rejected,
                Rejection = RejectionReason.MalformedObject,
            };
        }

        DocumentCategory category = DocumentClassifier.Classify(analysis, options.Thresholds);

        if (options.WritePerFileJson)
        {
            File.WriteAllText(Path.Combine(perFileRoot, id + ".json"), DocumentJson.Write(analysis, indent: false));
        }

        bool ocrPresent = analysis.TextRuns.Any(r => r.IsOcrLayer);
        bool formPresent = analysis.Form.HasAcroForm && analysis.Form.Fields.Count > 0;

        var dpis = new List<double>();
        int below = 0;
        foreach (ImageDpiInfo image in analysis.Images)
        {
            if (image.EffectiveDpi > 0)
            {
                dpis.Add(image.EffectiveDpi);
            }

            if (image.IsBelowThreshold)
            {
                below++;
            }
        }

        bool encryptedUnreadable = analysis.Security.IsEncrypted
            && analysis.Security.Authentication == AuthenticationResult.Failed;

        return new FileOutcome
        {
            Id = id,
            InputHash = contentHash,
            Status = analysis.Status,
            Category = category,
            PageCount = analysis.PageCount,
            OcrPresent = ocrPresent,
            FormPresent = formPresent,
            EncryptionAlgorithm = analysis.Security.Algorithm,
            Encrypted = analysis.Security.IsEncrypted,
            EncryptedUnreadable = encryptedUnreadable,
            Rejection = analysis.Rejection?.Reason ?? RejectionReason.None,
            Threshold = options.DpiThreshold,
            BelowThresholdCount = below,
            EffectiveDpis = dpis,
        };
    }

    private static Dictionary<string, FileOutcome> LoadLedger(string ledgerPath)
    {
        var map = new Dictionary<string, FileOutcome>(StringComparer.Ordinal);
        if (!File.Exists(ledgerPath))
        {
            return map;
        }

        foreach (string line in File.ReadAllLines(ledgerPath))
        {
            if (line.Length == 0)
            {
                continue;
            }

            FileOutcome? outcome = FileOutcome.FromLedgerLine(line);
            if (outcome != null)
            {
                map[outcome.Id] = outcome; // last entry wins
            }
        }

        return map;
    }
}
