using UnityRestartTool.Settings;

namespace UnityRestartTool.Tests;

public sealed class SchedulePlannerTests
{
    [Fact]
    public void ShouldTrigger_OnlyDuringConfiguredMinuteAndOncePerDay()
    {
        AppSettings settings = new()
        {
            ScheduleEnabled = true,
            ScheduleTime = "04:00",
        };

        Assert.True(SchedulePlanner.ShouldTrigger(settings, new DateTime(2026, 8, 24, 4, 0, 30)));
        Assert.False(SchedulePlanner.ShouldTrigger(settings, new DateTime(2026, 8, 24, 4, 1, 0)));

        settings.LastScheduledTriggerDate = "2026-08-24";
        Assert.False(SchedulePlanner.ShouldTrigger(settings, new DateTime(2026, 8, 24, 4, 0, 45)));
    }

    [Fact]
    public void NextOccurrence_DoesNotCatchUpAfterMissedTime()
    {
        AppSettings settings = new()
        {
            ScheduleEnabled = true,
            ScheduleTime = "04:00",
        };

        DateTime next = SchedulePlanner.NextOccurrence(
            settings,
            new DateTime(2026, 8, 24, 9, 30, 0));

        Assert.Equal(new DateTime(2026, 8, 25, 4, 0, 0), next);
    }
}
