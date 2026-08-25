using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityRestartTool.Interop;

namespace UnityRestartTool.Services;

internal sealed class WindowService
{
    public IReadOnlyList<(IntPtr Handle, int ProcessId, int Order)> EnumerateVisibleWindows()
    {
        List<(IntPtr Handle, int ProcessId, int Order)> windows = [];
        int order = 0;
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (NativeMethods.IsWindowVisible(handle))
            {
                NativeMethods.GetWindowThreadProcessId(handle, out uint processId);
                if (processId > 0)
                {
                    windows.Add((handle, checked((int)processId), order));
                }
            }

            order++;
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    public IntPtr FindMainWindow(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            process.Refresh();
            IntPtr declaredMainWindow = process.MainWindowHandle;
            if (declaredMainWindow != IntPtr.Zero &&
                NativeMethods.IsWindow(declaredMainWindow) &&
                NativeMethods.IsWindowVisible(declaredMainWindow) &&
                !string.IsNullOrWhiteSpace(NativeMethods.ReadWindowTitle(declaredMainWindow)))
            {
                return declaredMainWindow;
            }
        }
        catch (ArgumentException)
        {
            return IntPtr.Zero;
        }

        return EnumerateVisibleWindows()
            .Where(window => window.ProcessId == processId)
            .Select(window => window.Handle)
            .FirstOrDefault(handle => !string.IsNullOrWhiteSpace(NativeMethods.ReadWindowTitle(handle)));
    }

    public int GetWindowOrder(IntPtr windowHandle) => EnumerateVisibleWindows()
        .FirstOrDefault(window => window.Handle == windowHandle).Order;

    public bool IsForegroundProcess(int processId)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(foreground, out uint foregroundProcessId);
        return foregroundProcessId == processId;
    }

    public bool HasDisabledTopLevelWindow(int processId) =>
        EnumerateVisibleWindows().Any(window =>
            window.ProcessId == processId && !NativeMethods.IsWindowEnabled(window.Handle));

    public bool RequestGracefulClose(IntPtr windowHandle) =>
        NativeMethods.IsWindow(windowHandle) &&
        NativeMethods.PostMessageW(
            windowHandle,
            NativeMethods.WmClose,
            IntPtr.Zero,
            IntPtr.Zero);

    public WindowSnapshot? Capture(IntPtr windowHandle, int order)
    {
        if (!NativeMethods.IsWindow(windowHandle))
        {
            return null;
        }

        WindowPlacement placement = WindowPlacement.Create();
        if (!NativeMethods.GetWindowPlacement(windowHandle, ref placement))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetWindowPlacement failed.");
        }

        MonitorSnapshot? monitor = TryGetMonitorSnapshot(
            NativeMethods.MonitorFromWindow(
                windowHandle,
                NativeMethods.MonitorDefaultToNearest));

        return new WindowSnapshot(
            windowHandle,
            placement,
            order,
            NativeMethods.ReadWindowTitle(windowHandle),
            monitor);
    }

    public IReadOnlyList<MonitorSnapshot> EnumerateMonitors()
    {
        List<MonitorSnapshot> monitors = [];
        NativeMethods.MonitorEnumProc callback = (
            IntPtr monitorHandle,
            IntPtr deviceContext,
            ref NativeRect monitorRect,
            IntPtr parameter) =>
        {
            MonitorSnapshot? monitor = TryGetMonitorSnapshot(monitorHandle);
            if (monitor is not null && monitors.All(existing =>
                    !string.Equals(
                        existing.DeviceName,
                        monitor.DeviceName,
                        StringComparison.OrdinalIgnoreCase)))
            {
                monitors.Add(monitor);
            }

            return true;
        };

        NativeMethods.EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            callback,
            IntPtr.Zero);
        return monitors;
    }

    public void Restore(IntPtr windowHandle, WindowSnapshot snapshot)
    {
        if (!NativeMethods.IsWindow(windowHandle))
        {
            throw new InvalidOperationException("新编辑器主窗口已经消失。");
        }

        WindowPlacement placement = snapshot.Placement;
        if (snapshot.Monitor is not null)
        {
            MonitorSnapshot? targetMonitor = WindowPlacementMapper.SelectTargetMonitor(
                EnumerateMonitors(),
                snapshot.Monitor);
            if (targetMonitor is not null)
            {
                placement.NormalPosition = WindowPlacementMapper.MapNormalPosition(
                    placement.NormalPosition,
                    snapshot.Monitor,
                    targetMonitor);
            }
        }

        placement.Length = Marshal.SizeOf<WindowPlacement>();
        if (!NativeMethods.SetWindowPlacement(windowHandle, ref placement))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowPlacement failed.");
        }
    }

    private static MonitorSnapshot? TryGetMonitorSnapshot(IntPtr monitorHandle)
    {
        if (monitorHandle == IntPtr.Zero)
        {
            return null;
        }

        MonitorInfoEx monitorInfo = MonitorInfoEx.Create();
        if (!NativeMethods.GetMonitorInfoW(monitorHandle, ref monitorInfo))
        {
            return null;
        }

        string deviceName = monitorInfo.DeviceName?.TrimEnd('\0') ?? string.Empty;
        return string.IsNullOrWhiteSpace(deviceName)
            ? null
            : new MonitorSnapshot(deviceName, monitorInfo.Monitor, monitorInfo.Work);
    }
}

internal sealed record WindowSnapshot(
    IntPtr OriginalHandle,
    WindowPlacement Placement,
    int Order,
    string Title,
    MonitorSnapshot? Monitor);
