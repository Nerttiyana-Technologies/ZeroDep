using Xunit;

namespace ZeroDep.Lexing.Tests;

public sealed class ModuleMarkerTests
{
    [Fact]
    public void MilestoneIsExpected()
    {
        Assert.Equal("M1", ModuleMarker.Milestone);
    }
}
