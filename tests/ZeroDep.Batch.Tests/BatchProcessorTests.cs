using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace ZeroDep.Batch.Tests;

public sealed class BatchProcessorTests : IDisposable
{
    private readonly string _root;
    private readonly string _input;
    private readonly string _output;

    public BatchProcessorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "zerodep-batch-" + Guid.NewGuid().ToString("N"));
        _input = Path.Combine(_root, "in");
        _output = Path.Combine(_root, "out");
        Directory.CreateDirectory(_input);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public async Task RunProducesLedgerAggregateAndPerFileOutputs()
    {
        File.WriteAllBytes(Path.Combine(_input, "doc1.pdf"), TextPdf("BT /F1 12 Tf 100 700 Td (HelloZeroDep) Tj ET"));
        File.WriteAllBytes(Path.Combine(_input, "doc2.pdf"), TextPdf("BT /F1 12 Tf 100 700 Td (SecondDoc) Tj ET"));
        File.WriteAllBytes(Path.Combine(_input, "bad.pdf"), Encoding.ASCII.GetBytes("not a pdf at all, no header here"));

        var options = new BatchOptions { InputDirectory = _input, OutputDirectory = _output, MaxConcurrency = 2 };
        BatchSummary summary = await new BatchProcessor().RunAsync(options);

        Assert.Equal(3, summary.Total);
        Assert.Equal(3, summary.ProcessedThisRun);
        Assert.Equal(0, summary.Skipped);
        Assert.True(summary.Rejected >= 1);

        Assert.True(File.Exists(Path.Combine(_output, "ledger.tsv")));
        Assert.True(File.Exists(summary.AggregatePath));
        Assert.Equal(3, Directory.GetFiles(Path.Combine(_output, "perfile"), "*.json").Length);
    }

    [Fact]
    public async Task AggregateOutputContainsNoFileNamesOrContent()
    {
        File.WriteAllBytes(Path.Combine(_input, "secret-invoice.pdf"), TextPdf("BT /F1 12 Tf 100 700 Td (ConfidentialText) Tj ET"));

        var options = new BatchOptions { InputDirectory = _input, OutputDirectory = _output };
        BatchSummary summary = await new BatchProcessor().RunAsync(options);

        string aggregate = File.ReadAllText(summary.AggregatePath);
        Assert.DoesNotContain("secret-invoice", aggregate, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConfidentialText", aggregate, StringComparison.Ordinal);
        Assert.DoesNotContain(".pdf", aggregate, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SecondRunResumesAndSkipsCompletedFiles()
    {
        File.WriteAllBytes(Path.Combine(_input, "doc1.pdf"), TextPdf("BT /F1 12 Tf 100 700 Td (Doc) Tj ET"));

        var options = new BatchOptions { InputDirectory = _input, OutputDirectory = _output };
        BatchSummary first = await new BatchProcessor().RunAsync(options);
        Assert.Equal(1, first.ProcessedThisRun);

        BatchSummary second = await new BatchProcessor().RunAsync(options);
        Assert.Equal(0, second.ProcessedThisRun);
        Assert.Equal(1, second.Skipped);
        // aggregate still reflects the full corpus after a resumed run
        Assert.Equal(1, second.Statistics.Corpus.Total);
    }

    private static byte[] TextPdf(string content)
    {
        var ms = new MemoryStream();
        void W(string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s);
            ms.Write(b, 0, b.Length);
        }

        W("%PDF-1.4\n");
        long o1 = ms.Position; W("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        long o2 = ms.Position; W("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] >>\nendobj\n");
        long o3 = ms.Position; W("3 0 obj\n<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>\nendobj\n");
        long o4 = ms.Position; W($"4 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");
        long o5 = ms.Position; W("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        long xref = ms.Position;
        W("xref\n0 6\n0000000000 65535 f \n");
        foreach (long o in new[] { o1, o2, o3, o4, o5 })
        {
            W($"{o:D10} 00000 n \n");
        }

        W("trailer\n<< /Size 6 /Root 1 0 R >>\n");
        W($"startxref\n{xref}\n%%EOF");
        return ms.ToArray();
    }
}
