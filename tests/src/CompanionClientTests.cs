using UnityRestartTool.Services;

namespace UnityRestartTool.Tests;

public sealed class CompanionClientTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("1.0.0", false)]
    [InlineData("1.0.1", false)]
    [InlineData("1.0.2", true)]
    [InlineData("1.1.0", true)]
    [InlineData("invalid", false)]
    public void IsSupportedCompanionVersion_RequiresFixedCompanionOrNewer(
        string? version,
        bool expected)
    {
        Assert.Equal(expected, CompanionClient.IsSupportedCompanionVersion(version));
    }
}
