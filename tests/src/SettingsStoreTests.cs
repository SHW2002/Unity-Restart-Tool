using UnityRestartTool.Settings;

namespace UnityRestartTool.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "UnityRestartTool.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveAndLoad_PreservesProjectPolicyCaseInsensitively()
    {
        SettingsStore store = new(_temporaryDirectory);
        AppSettings settings = new()
        {
            ScheduleEnabled = true,
            ScheduleTime = "05:45",
        };
        settings.Projects["E:\\Unity\\Garden"] = new ProjectPolicy { IncludeInSchedule = true };

        store.Save(settings);
        AppSettings loaded = store.Load();

        Assert.True(loaded.ScheduleEnabled);
        Assert.Equal("05:45", loaded.ScheduleTime);
        Assert.True(loaded.Projects["e:\\unity\\garden"].IncludeInSchedule);
        Assert.False(File.Exists(Path.Combine(_temporaryDirectory, "config.json.tmp")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, true);
        }
    }
}
