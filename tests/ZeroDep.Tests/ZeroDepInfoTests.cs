using Xunit;

namespace ZeroDep.Tests;

public sealed class ZeroDepInfoTests
{
    [Fact]
    public void VersionStartsWithExpectedPrefix()
    {
        Assert.StartsWith("1.2.0", ZeroDepInfo.Version);
    }
}
