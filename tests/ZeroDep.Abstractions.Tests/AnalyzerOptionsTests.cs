using Xunit;
using ZeroDep.Abstractions;

namespace ZeroDep.Abstractions.Tests;

public sealed class AnalyzerOptionsTests
{
    [Fact]
    public void DefaultDpiThresholdIs150()
    {
        Assert.Equal(150, new AnalyzerOptions().DpiThreshold);
    }

    [Fact]
    public void RejectionReasonDefaultsToNone()
    {
        Assert.Equal(RejectionReason.None, default(RejectionReason));
    }

    [Fact]
    public void EncryptionAlgorithmDefaultsToNone()
    {
        Assert.Equal(EncryptionAlgorithm.None, default(EncryptionAlgorithm));
    }
}
