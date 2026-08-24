namespace UnityRestartTool.Settings;

internal sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public bool ScheduleEnabled { get; set; }
    public string ScheduleTime { get; set; } = "04:00";
    public string? LastScheduledTriggerDate { get; set; }
    public bool StartWithWindows { get; set; }
    public bool StartMinimizedToTray { get; set; } = true;
    public Dictionary<string, ProjectPolicy> Projects { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class ProjectPolicy
{
    public bool IncludeInSchedule { get; set; }
}
