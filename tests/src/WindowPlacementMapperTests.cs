using UnityRestartTool.Interop;
using UnityRestartTool.Services;

namespace UnityRestartTool.Tests;

public sealed class WindowPlacementMapperTests
{
    [Fact]
    public void WindowService_EnumeratesCurrentMonitors()
    {
        WindowService service = new();

        IReadOnlyList<MonitorSnapshot> monitors = service.EnumerateMonitors();

        Assert.NotEmpty(monitors);
        Assert.All(monitors, monitor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(monitor.DeviceName));
            Assert.True(monitor.MonitorArea.Right > monitor.MonitorArea.Left);
            Assert.True(monitor.MonitorArea.Bottom > monitor.MonitorArea.Top);
        });
    }

    [Fact]
    public void MapNormalPosition_WithUnchangedWorkArea_PreservesExactRectangle()
    {
        MonitorSnapshot monitor = CreateMonitor("DISPLAY1", 0, 0, 1920, 1080);
        NativeRect original = Rect(120, 80, 960, 760);

        NativeRect mapped = WindowPlacementMapper.MapNormalPosition(
            original,
            monitor,
            monitor);

        AssertRect(original, mapped);
    }

    [Fact]
    public void MapNormalPosition_WhenMonitorMoves_TranslatesRectangle()
    {
        MonitorSnapshot source = CreateMonitor("DISPLAY1", 0, 0, 1920, 1080);
        MonitorSnapshot target = CreateMonitor("DISPLAY2", -1920, 0, 0, 1080);

        NativeRect mapped = WindowPlacementMapper.MapNormalPosition(
            Rect(120, 80, 960, 760),
            source,
            target);

        AssertRect(Rect(-1800, 80, -960, 760), mapped);
    }

    [Fact]
    public void MapNormalPosition_WhenMonitorResizes_ScalesAndClampsRectangle()
    {
        MonitorSnapshot source = CreateMonitor("DISPLAY1", 0, 0, 1920, 1080);
        MonitorSnapshot target = CreateMonitor("DISPLAY1", 0, 0, 2560, 1440);

        NativeRect mapped = WindowPlacementMapper.MapNormalPosition(
            Rect(120, 80, 960, 760),
            source,
            target);

        AssertRect(Rect(160, 107, 1280, 1013), mapped);
    }

    [Fact]
    public void SelectTargetMonitor_PrefersDeviceNameThenNearestCenter()
    {
        MonitorSnapshot original = CreateMonitor("DISPLAY9", 1920, 0, 3840, 1080);
        MonitorSnapshot left = CreateMonitor("DISPLAY1", -1920, 0, 0, 1080);
        MonitorSnapshot right = CreateMonitor("DISPLAY2", 0, 0, 1920, 1080);

        MonitorSnapshot? nearest = WindowPlacementMapper.SelectTargetMonitor(
            [left, right],
            original);
        Assert.Same(right, nearest);

        MonitorSnapshot matching = CreateMonitor("DISPLAY9", -3840, 0, -1920, 1080);
        MonitorSnapshot? byDevice = WindowPlacementMapper.SelectTargetMonitor(
            [left, matching, right],
            original);
        Assert.Same(matching, byDevice);
    }

    private static MonitorSnapshot CreateMonitor(
        string deviceName,
        int left,
        int top,
        int right,
        int bottom) =>
        new(
            deviceName,
            Rect(left, top, right, bottom),
            Rect(left, top, right, bottom));

    private static NativeRect Rect(int left, int top, int right, int bottom) => new()
    {
        Left = left,
        Top = top,
        Right = right,
        Bottom = bottom,
    };

    private static void AssertRect(NativeRect expected, NativeRect actual)
    {
        Assert.Equal(expected.Left, actual.Left);
        Assert.Equal(expected.Top, actual.Top);
        Assert.Equal(expected.Right, actual.Right);
        Assert.Equal(expected.Bottom, actual.Bottom);
    }
}
