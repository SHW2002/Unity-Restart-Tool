using UnityRestartTool.Interop;

namespace UnityRestartTool.Services;

internal sealed record MonitorSnapshot(
    string DeviceName,
    NativeRect MonitorArea,
    NativeRect WorkArea);

internal static class WindowPlacementMapper
{
    public static NativeRect MapNormalPosition(
        NativeRect sourcePosition,
        MonitorSnapshot sourceMonitor,
        MonitorSnapshot targetMonitor)
    {
        if (RectEquals(sourceMonitor.WorkArea, targetMonitor.WorkArea))
        {
            return sourcePosition;
        }

        NativeRect sourceArea = GetUsableArea(sourceMonitor);
        NativeRect targetArea = GetUsableArea(targetMonitor);
        if (!HasPositiveSize(sourceArea) || !HasPositiveSize(targetArea))
        {
            return sourcePosition;
        }

        NativeRect mapped = new()
        {
            Left = ScaleEdge(
                sourcePosition.Left,
                sourceArea.Left,
                sourceArea.Right,
                targetArea.Left,
                targetArea.Right),
            Top = ScaleEdge(
                sourcePosition.Top,
                sourceArea.Top,
                sourceArea.Bottom,
                targetArea.Top,
                targetArea.Bottom),
            Right = ScaleEdge(
                sourcePosition.Right,
                sourceArea.Left,
                sourceArea.Right,
                targetArea.Left,
                targetArea.Right),
            Bottom = ScaleEdge(
                sourcePosition.Bottom,
                sourceArea.Top,
                sourceArea.Bottom,
                targetArea.Top,
                targetArea.Bottom),
        };
        return ClampToArea(mapped, targetArea);
    }

    public static MonitorSnapshot? SelectTargetMonitor(
        IReadOnlyList<MonitorSnapshot> availableMonitors,
        MonitorSnapshot originalMonitor)
    {
        if (availableMonitors.Count == 0)
        {
            return null;
        }

        MonitorSnapshot? sameDevice = availableMonitors.FirstOrDefault(monitor =>
            string.Equals(
                monitor.DeviceName,
                originalMonitor.DeviceName,
                StringComparison.OrdinalIgnoreCase));
        if (sameDevice is not null)
        {
            return sameDevice;
        }

        double originalCenterX = Center(
            originalMonitor.MonitorArea.Left,
            originalMonitor.MonitorArea.Right);
        double originalCenterY = Center(
            originalMonitor.MonitorArea.Top,
            originalMonitor.MonitorArea.Bottom);
        return availableMonitors
            .OrderBy(monitor => DistanceSquared(
                originalCenterX,
                originalCenterY,
                Center(monitor.MonitorArea.Left, monitor.MonitorArea.Right),
                Center(monitor.MonitorArea.Top, monitor.MonitorArea.Bottom)))
            .First();
    }

    internal static NativeRect ClampToArea(NativeRect rectangle, NativeRect area)
    {
        if (!HasPositiveSize(area))
        {
            return rectangle;
        }

        int areaWidth = area.Right - area.Left;
        int areaHeight = area.Bottom - area.Top;
        int width = Math.Clamp(rectangle.Right - rectangle.Left, 1, areaWidth);
        int height = Math.Clamp(rectangle.Bottom - rectangle.Top, 1, areaHeight);
        int left = Math.Clamp(rectangle.Left, area.Left, area.Right - width);
        int top = Math.Clamp(rectangle.Top, area.Top, area.Bottom - height);
        return new NativeRect
        {
            Left = left,
            Top = top,
            Right = left + width,
            Bottom = top + height,
        };
    }

    private static NativeRect GetUsableArea(MonitorSnapshot monitor) =>
        HasPositiveSize(monitor.WorkArea) ? monitor.WorkArea : monitor.MonitorArea;

    private static int ScaleEdge(
        int value,
        int sourceStart,
        int sourceEnd,
        int targetStart,
        int targetEnd)
    {
        double sourceSize = sourceEnd - (double)sourceStart;
        double targetSize = targetEnd - (double)targetStart;
        double ratio = (value - sourceStart) / sourceSize;
        return targetStart + (int)Math.Round(
            ratio * targetSize,
            MidpointRounding.AwayFromZero);
    }

    private static bool HasPositiveSize(NativeRect rectangle) =>
        rectangle.Right > rectangle.Left && rectangle.Bottom > rectangle.Top;

    private static bool RectEquals(NativeRect left, NativeRect right) =>
        left.Left == right.Left &&
        left.Top == right.Top &&
        left.Right == right.Right &&
        left.Bottom == right.Bottom;

    private static double Center(int start, int end) =>
        start + (end - (double)start) / 2;

    private static double DistanceSquared(
        double leftX,
        double leftY,
        double rightX,
        double rightY)
    {
        double deltaX = leftX - rightX;
        double deltaY = leftY - rightY;
        return deltaX * deltaX + deltaY * deltaY;
    }
}
