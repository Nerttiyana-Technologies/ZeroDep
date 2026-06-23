using Xunit;

namespace ZeroDep.IO.Tests;

public sealed class ModuleMarkerTests
{
    [Fact]
    public void MilestoneIsExpected()
    {
        Assert.Equal("M1", ModuleMarker.Milestone);
    }
}
