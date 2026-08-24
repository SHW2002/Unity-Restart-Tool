using Microsoft.Win32;

namespace UnityRestartTool.Services;

internal static class StartupRegistration
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "UnityRestartTool";

    public static void SetEnabled(bool enabled, bool startMinimized)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath, true);
        if (!enabled)
        {
            key.DeleteValue(ValueName, false);
            return;
        }

        string executable = Environment.ProcessPath ?? Application.ExecutablePath;
        string arguments = startMinimized ? " --tray" : string.Empty;
        key.SetValue(ValueName, $"\"{executable}\"{arguments}", RegistryValueKind.String);
    }

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath, false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }
}
