using Xunit;

namespace ZeroDep.Model.Tests;

public sealed class ModuleMarkerTests
{
    [Fact]
    public void MilestoneIsExpected()
    {
        Assert.Equal("M2", ModuleMarker.Milestone);
    }
}
