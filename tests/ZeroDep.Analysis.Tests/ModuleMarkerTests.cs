using Xunit;

namespace ZeroDep.Analysis.Tests;

public sealed class ModuleMarkerTests
{
    [Fact]
    public void MilestoneIsExpected()
    {
        Assert.Equal("M3", ModuleMarker.Milestone);
    }
}
