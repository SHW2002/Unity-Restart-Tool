namespace UnityRestartTool.Models;

internal enum CompanionHealth
{
    NotInstalled,
    Starting,
    Ready,
    Stale,
    Incompatible,
    Error,
}

internal sealed record CompanionState(
    CompanionHealth Health,
    string Message,
    int? ProcessId = null,
    int? ProtocolVersion = null,
    DateTime? HeartbeatUtc = null)
{
    public bool CanRestart => Health == CompanionHealth.Ready;
}
