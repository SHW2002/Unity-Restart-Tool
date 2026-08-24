using System.Globalization;

namespace UnityRestartTool.Settings;

internal static class SchedulePlanner
{
    public static bool TryParseTime(string value, out TimeOnly time) =>
        TimeOnly.TryParseExact(
            value,
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time);

    public static bool ShouldTrigger(AppSettings settings, DateTime now)
    {
        if (!settings.ScheduleEnabled ||
            !TryParseTime(settings.ScheduleTime, out TimeOnly scheduledTime) ||
            scheduledTime.Hour != now.Hour ||
            scheduledTime.Minute != now.Minute)
        {
            return false;
        }

        return !string.Equals(
            settings.LastScheduledTriggerDate,
            DateOnly.FromDateTime(now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    public static DateTime NextOccurrence(AppSettings settings, DateTime now)
    {
        if (!TryParseTime(settings.ScheduleTime, out TimeOnly scheduledTime))
        {
            scheduledTime = new TimeOnly(4, 0);
        }

        DateTime candidate = now.Date.Add(scheduledTime.ToTimeSpan());
        string today = DateOnly.FromDateTime(now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (candidate <= now || string.Equals(settings.LastScheduledTriggerDate, today, StringComparison.Ordinal))
        {
            candidate = candidate.AddDays(1);
        }

        return candidate;
    }
}
