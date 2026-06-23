using Xunit;

namespace ZeroDep.Validation.Tests;

public sealed class ModuleMarkerTests
{
    [Fact]
    public void MilestoneIsExpected()
    {
        Assert.Equal("M1.5", ModuleMarker.Milestone);
    }
}
