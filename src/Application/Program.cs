using UnityRestartTool.Infrastructure;
using UnityRestartTool.Settings;
using UnityRestartTool.UI;

namespace UnityRestartTool;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        SettingsStore settingsStore = new();
        AppSettings settings = settingsStore.Load();
        AppLogger logger = new();

        Application.ThreadException += (_, eventArgs) =>
            logger.Error("UI", "未处理的界面异常", eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            logger.Error("Runtime", "未处理的运行时异常", eventArgs.ExceptionObject as Exception);

        bool startInTray = args.Any(argument =>
            string.Equals(argument, "--tray", StringComparison.OrdinalIgnoreCase));
        Application.Run(new MainForm(settings, settingsStore, logger, startInTray));
    }
}
