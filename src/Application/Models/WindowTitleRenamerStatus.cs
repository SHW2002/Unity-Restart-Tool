namespace UnityRestartTool.Models;

internal enum WindowTitleRenamerHealth
{
    Ready,
    NotRunning,
    Incompatible,
    Unavailable,
}

internal sealed record WindowTitleRenamerStatus(
    WindowTitleRenamerHealth Health,
    string Message,
    Version? DetectedVersion = null);
