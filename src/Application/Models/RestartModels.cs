namespace UnityRestartTool.Models;

internal enum RestartTrigger
{
    Manual,
    Scheduled,
}

internal enum RestartStage
{
    Pending,
    Preflight,
    Skipped,
    Stopping,
    Starting,
    RestoringWindow,
    RestoringTitle,
    Completed,
    Failed,
}

internal sealed record RestartProgress(
    string ProjectPath,
    RestartStage Stage,
    string Message,
    bool IsError = false);

internal sealed record PreflightResult(
    bool Eligible,
    string Reason,
    int ConsoleEntryCount,
    IReadOnlyList<string> DirtyScenes,
    IReadOnlyList<string> DirtyAssets,
    IReadOnlyList<string> BusyReasons);

internal sealed record RestartInstanceResult(
    string ProjectPath,
    bool Succeeded,
    bool Skipped,
    string Message);

internal sealed record RestartBatchResult(
    DateTime StartedAt,
    DateTime FinishedAt,
    IReadOnlyList<RestartInstanceResult> Instances)
{
    public int SucceededCount => Instances.Count(instance => instance.Succeeded);
    public int SkippedCount => Instances.Count(instance => instance.Skipped);
    public int FailedCount => Instances.Count(instance => !instance.Succeeded && !instance.Skipped);
}
