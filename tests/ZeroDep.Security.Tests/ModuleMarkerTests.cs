using Xunit;

namespace ZeroDep.Security.Tests;

public sealed class ModuleMarkerTests
{
    [Fact]
    public void MilestoneIsExpected()
    {
        Assert.Equal("M2.5", ModuleMarker.Milestone);
    }
}
