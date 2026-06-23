using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ZeroDep.Abstractions;

namespace ZeroDep.Batch;

/// <summary>
/// A compact, content-free per-file result. It carries only aggregation-relevant structural scalars
/// (status, category, counts, DPI values) — never file names, extracted text, or field values — and
/// round-trips through the resumable ledger.
/// </summary>
internal sealed class FileOutcome
{
    private const char FieldSeparator = '\t';
    private const char DpiSeparator = ',';
    private const int FieldCount = 14;

    public string Id { get; set; } = string.Empty;

    public string InputHash { get; set; } = string.Empty;

    public DocumentStatus Status { get; set; }

    public DocumentCategory Category { get; set; }

    public int PageCount { get; set; }

    public bool OcrPresent { get; set; }

    public bool FormPresent { get; set; }

    public EncryptionAlgorithm EncryptionAlgorithm { get; set; }

    public bool Encrypted { get; set; }

    public bool EncryptedUnreadable { get; set; }

    public RejectionReason Rejection { get; set; }

    public int Threshold { get; set; }

    public int BelowThresholdCount { get; set; }

    public IReadOnlyList<double> EffectiveDpis { get; set; } = Array.Empty<double>();

    /// <summary>Serializes this outcome to a single tab-delimited ledger line.</summary>
    public string ToLedgerLine()
    {
        string dpis = EffectiveDpis.Count == 0
            ? string.Empty
            : string.Join(
                DpiSeparator.ToString(),
                EffectiveDpis.Select(d => ((int)Math.Round(d)).ToString(CultureInfo.InvariantCulture)));

        return string.Join(FieldSeparator.ToString(), new[]
        {
            Id,
            InputHash,
            Status.ToString(),
            Category.ToString(),
            PageCount.ToString(CultureInfo.InvariantCulture),
            OcrPresent ? "1" : "0",
            FormPresent ? "1" : "0",
            EncryptionAlgorithm.ToString(),
            Encrypted ? "1" : "0",
            EncryptedUnreadable ? "1" : "0",
            Rejection.ToString(),
            Threshold.ToString(CultureInfo.InvariantCulture),
            BelowThresholdCount.ToString(CultureInfo.InvariantCulture),
            dpis,
        });
    }

    /// <summary>Parses a ledger line back into an outcome, or returns null if malformed.</summary>
    public static FileOutcome? FromLedgerLine(string line)
    {
        string[] parts = line.Split(FieldSeparator);
        if (parts.Length < FieldCount)
        {
            return null;
        }

        var outcome = new FileOutcome
        {
            Id = parts[0],
            InputHash = parts[1],
            Status = Enum.TryParse(parts[2], out DocumentStatus status) ? status : DocumentStatus.Processed,
            Category = Enum.TryParse(parts[3], out DocumentCategory category) ? category : DocumentCategory.Unknown,
            PageCount = ParseInt(parts[4]),
            OcrPresent = parts[5] == "1",
            FormPresent = parts[6] == "1",
            EncryptionAlgorithm = Enum.TryParse(parts[7], out EncryptionAlgorithm algorithm) ? algorithm : EncryptionAlgorithm.None,
            Encrypted = parts[8] == "1",
            EncryptedUnreadable = parts[9] == "1",
            Rejection = Enum.TryParse(parts[10], out RejectionReason rejection) ? rejection : RejectionReason.None,
            Threshold = ParseInt(parts[11]),
            BelowThresholdCount = ParseInt(parts[12]),
        };

        if (parts[13].Length > 0)
        {
            var dpis = new List<double>();
            foreach (string token in parts[13].Split(DpiSeparator))
            {
                if (double.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out double value))
                {
                    dpis.Add(value);
                }
            }

            outcome.EffectiveDpis = dpis;
        }

        return outcome;
    }

    private static int ParseInt(string text)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
}
