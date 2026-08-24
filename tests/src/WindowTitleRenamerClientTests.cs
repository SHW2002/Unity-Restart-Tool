using UnityRestartTool.Services;

namespace UnityRestartTool.Tests;

public sealed class WindowTitleRenamerClientTests
{
    [Theory]
    [InlineData("2.0.0.0", 2, 0, 0, 0)]
    [InlineData("2.1.3-beta", 2, 1, 3, -1)]
    [InlineData(" 3.0 ", 3, 0, -1, -1)]
    public void ParseVersion_AcceptsFileVersionFormats(
        string value,
        int major,
        int minor,
        int build,
        int revision)
    {
        Version? version = WindowTitleRenamerClient.ParseVersion(value);

        Assert.NotNull(version);
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(build, version.Build);
        Assert.Equal(revision, version.Revision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("release")]
    public void ParseVersion_RejectsMissingOrInvalidValues(string? value)
    {
        Assert.Null(WindowTitleRenamerClient.ParseVersion(value));
    }

    [Theory]
    [InlineData(1, 9, 9, false)]
    [InlineData(2, 0, 0, true)]
    [InlineData(2, 1, 0, true)]
    public void IsSupportedVersion_RequiresVersionTwoOrNewer(
        int major,
        int minor,
        int build,
        bool expected)
    {
        Assert.Equal(
            expected,
            WindowTitleRenamerClient.IsSupportedVersion(new Version(major, minor, build)));
    }

    [Fact]
    public void SelectRestoreRule_WithPersistentRule_PrefersSavedTitle()
    {
        PersistentTitleRule? rule = WindowTitleRenamerClient.SelectRestoreRule(
            true,
            "已保存标题",
            "当前窗口标题");

        Assert.NotNull(rule);
        Assert.Equal("已保存标题", rule.Title);
    }

    [Fact]
    public void SelectRestoreRule_WithoutPersistentRule_UsesCurrentWindowTitle()
    {
        PersistentTitleRule? rule = WindowTitleRenamerClient.SelectRestoreRule(
            false,
            null,
            "Unity 13 clone");

        Assert.NotNull(rule);
        Assert.Equal("Unity 13 clone", rule.Title);
    }

    [Fact]
    public void SelectRestoreRule_WithoutAnyUsableTitle_ReturnsNull()
    {
        Assert.Null(WindowTitleRenamerClient.SelectRestoreRule(false, null, " "));
    }
}
