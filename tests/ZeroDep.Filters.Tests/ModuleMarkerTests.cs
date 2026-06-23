using Xunit;

namespace ZeroDep.Filters.Tests;

public sealed class ModuleMarkerTests
{
    [Fact]
    public void MilestoneIsExpected()
    {
        Assert.Equal("M1", ModuleMarker.Milestone);
    }
}
