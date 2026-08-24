using System.Diagnostics;
using System.Management;
using UnityRestartTool.Infrastructure;
using UnityRestartTool.Interop;
using UnityRestartTool.Models;

namespace UnityRestartTool.Services;

internal sealed class EditorDiscoveryService
{
    private readonly WindowService _windowService;
    private readonly AppLogger _logger;

    public EditorDiscoveryService(WindowService windowService, AppLogger logger)
    {
        _windowService = windowService;
        _logger = logger;
    }

    public IReadOnlyList<EditorInstance> Discover()
    {
        IReadOnlyList<(IntPtr Handle, int ProcessId, int Order)> windows =
            _windowService.EnumerateVisibleWindows();
        List<EditorInstance> instances = [];

        try
        {
            using ManagementObjectSearcher searcher = new(
                "SELECT ProcessId, Name, ExecutablePath, CommandLine FROM Win32_Process " +
                "WHERE Name='Unity.exe' OR Name='Tuanjie.exe'");
            using ManagementObjectCollection results = searcher.Get();
            foreach (ManagementObject result in results)
            {
                TryAddInstance(result, windows, instances);
            }
        }
        catch (Exception exception)
        {
            _logger.Error("发现", "枚举 Unity/团结进程失败", exception);
        }

        return instances
            .OrderBy(instance => instance.StartTime)
            .ThenBy(instance => instance.ProcessId)
            .ToArray();
    }

    private void TryAddInstance(
        ManagementObject result,
        IReadOnlyList<(IntPtr Handle, int ProcessId, int Order)> windows,
        ICollection<EditorInstance> instances)
    {
        try
        {
            int processId = Convert.ToInt32(result["ProcessId"]);
            string processName = Convert.ToString(result["Name"]) ?? string.Empty;
            string executablePath = Convert.ToString(result["ExecutablePath"]) ?? string.Empty;
            string commandLine = Convert.ToString(result["CommandLine"]) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(executablePath) ||
                ProcessCommandLineParser.IsWorkerOrBatchProcess(commandLine) ||
                !ProcessCommandLineParser.TryGetProjectPath(commandLine, out string projectPath) ||
                !IsUnityProject(projectPath))
            {
                return;
            }

            using Process process = Process.GetProcessById(processId);
            process.Refresh();
            IntPtr mainWindowHandle = process.MainWindowHandle;
            (IntPtr Handle, int ProcessId, int Order) window = windows.FirstOrDefault(candidate =>
                candidate.ProcessId == processId && candidate.Handle == mainWindowHandle);
            if (window.Handle == IntPtr.Zero)
            {
                window = windows.FirstOrDefault(candidate =>
                    candidate.ProcessId == processId &&
                    !string.IsNullOrWhiteSpace(NativeMethods.ReadWindowTitle(candidate.Handle)));
            }
            if (window.Handle == IntPtr.Zero)
            {
                return;
            }

            string editorVersion = ReadEditorVersion(projectPath);
            EditorKind kind = processName.Equals("Tuanjie.exe", StringComparison.OrdinalIgnoreCase)
                ? EditorKind.Tuanjie
                : EditorKind.Unity;
            instances.Add(new EditorInstance(
                processId,
                kind,
                Path.GetFullPath(executablePath),
                projectPath,
                new DirectoryInfo(projectPath).Name,
                editorVersion,
                process.StartTime,
                window.Handle,
                NativeMethods.ReadWindowTitle(window.Handle),
                window.Order));
        }
        catch (Exception exception)
        {
            _logger.Warning("发现", $"忽略无法读取的编辑器进程: {exception.Message}");
        }
    }

    private static bool IsUnityProject(string projectPath) =>
        Directory.Exists(Path.Combine(projectPath, "Assets")) &&
        Directory.Exists(Path.Combine(projectPath, "ProjectSettings"));

    private static string ReadEditorVersion(string projectPath)
    {
        string versionPath = Path.Combine(projectPath, "ProjectSettings", "ProjectVersion.txt");
        if (!File.Exists(versionPath))
        {
            return "未知";
        }

        string? line = File.ReadLines(versionPath)
            .FirstOrDefault(value => value.StartsWith("m_EditorVersion:", StringComparison.Ordinal));
        return line?.Split(':', 2)[1].Trim() ?? "未知";
    }
}
