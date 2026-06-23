using Xunit;

namespace ZeroDep.Json.Tests;

public sealed class ModuleMarkerTests
{
    [Fact]
    public void MilestoneIsExpected()
    {
        Assert.Equal("M7", ModuleMarker.Milestone);
    }
}
