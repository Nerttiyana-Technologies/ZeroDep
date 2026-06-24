using System;
using System.Collections.Generic;

namespace ZeroDep.Ocr;

/// <summary>
/// OCR accuracy metrics — Character Error Rate (CER) and Word Error Rate (WER) via Levenshtein edit
/// distance, normalized by the reference length. Lower is better; 0 means a perfect match. Useful for
/// benchmarking an <see cref="IOcrEngine"/> against a labeled ground-truth set.
/// </summary>
public static class OcrAccuracy
{
    /// <summary>The fraction of characters wrong (insertions + deletions + substitutions ÷ reference length).</summary>
    /// <param name="reference">The ground-truth text.</param>
    /// <param name="hypothesis">The OCR output to score.</param>
    public static double CharacterErrorRate(string? reference, string? hypothesis)
    {
        (int distance, int length) = CharacterDistance(reference, hypothesis);
        return length == 0 ? (distance == 0 ? 0.0 : 1.0) : (double)distance / length;
    }

    /// <summary>The fraction of words wrong (edit distance over whitespace-split tokens ÷ reference word count).</summary>
    /// <param name="reference">The ground-truth text.</param>
    /// <param name="hypothesis">The OCR output to score.</param>
    public static double WordErrorRate(string? reference, string? hypothesis)
    {
        (int distance, int length) = WordDistance(reference, hypothesis);
        return length == 0 ? (distance == 0 ? 0.0 : 1.0) : (double)distance / length;
    }

    /// <summary>
    /// The raw character edit distance and reference length, for computing a corpus-level (micro-averaged)
    /// CER as <c>Σ distance ÷ Σ length</c> — the standard way OCR character accuracy is reported.
    /// </summary>
    /// <param name="reference">The ground-truth text.</param>
    /// <param name="hypothesis">The OCR output to score.</param>
    public static (int Distance, int Length) CharacterDistance(string? reference, string? hypothesis)
    {
        char[] r = (reference ?? string.Empty).ToCharArray();
        char[] h = (hypothesis ?? string.Empty).ToCharArray();
        return (Levenshtein(r, h), r.Length);
    }

    /// <summary>
    /// The raw word edit distance and reference word count, for computing a corpus-level (micro-averaged)
    /// WER as <c>Σ distance ÷ Σ length</c>.
    /// </summary>
    /// <param name="reference">The ground-truth text.</param>
    /// <param name="hypothesis">The OCR output to score.</param>
    public static (int Distance, int Length) WordDistance(string? reference, string? hypothesis)
    {
        string[] r = Tokenize(reference);
        string[] h = Tokenize(hypothesis);
        return (Levenshtein(r, h), r.Length);
    }

    private static string[] Tokenize(string? s)
        => (s ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static int Levenshtein<T>(IReadOnlyList<T> a, IReadOnlyList<T> b)
    {
        int n = a.Count;
        int m = b.Count;
        var previous = new int[m + 1];
        var current = new int[m + 1];
        for (int j = 0; j <= m; j++)
        {
            previous[j] = j;
        }

        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        for (int i = 1; i <= n; i++)
        {
            current[0] = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = comparer.Equals(a[i - 1], b[j - 1]) ? 0 : 1;
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[m];
    }
}
