namespace UnityRestartTool.Infrastructure;

internal enum AppLogLevel
{
    Info,
    Warning,
    Error,
}

internal sealed record LogEntry(
    DateTime Timestamp,
    AppLogLevel Level,
    string Source,
    string Message);
