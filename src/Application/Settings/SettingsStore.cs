using System.Text.Json;

namespace UnityRestartTool.Settings;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _configDirectory;
    private readonly string _configPath;

    public SettingsStore(string? configDirectory = null)
    {
        _configDirectory = configDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UnityRestartTool");
        _configPath = Path.Combine(_configDirectory, "config.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                return new AppSettings();
            }

            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(_configPath), JsonOptions) ?? new AppSettings();
            settings.Projects = new Dictionary<string, ProjectPolicy>(
                settings.Projects ?? [], StringComparer.OrdinalIgnoreCase);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(_configDirectory);
        string temporaryPath = _configPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, _configPath, true);
    }
}
