using System.Diagnostics;
using UnityRestartTool.Infrastructure;
using UnityRestartTool.Models;
using UnityRestartTool.Services;

namespace UnityRestartTool.Tests;

public sealed class EditorRestartIntegrationTests
{
    [Fact]
    [Trait("Category", "EditorIntegration")]
    public async Task RestartAsync_ReplacesProcessAndRestoresWindowPlacement()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("UNITY_RESTART_RUN_EDITOR_INTEGRATION"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        string editorPath = RequireEnvironmentPath("UNITY_RESTART_EDITOR_PATH", File.Exists);
        string packagePath = RequireEnvironmentPath("UNITY_RESTART_COMPANION_PATH", Directory.Exists);
        string testRoot = Path.Combine(
            Path.GetTempPath(),
            "UnityRestartTool.Integration",
            Guid.NewGuid().ToString("N"));
        string projectPath = Path.Combine(testRoot, "Project");
        string logDirectory = Path.Combine(testRoot, "ToolLogs");
        Directory.CreateDirectory(testRoot);
        Process? activeEditor = null;

        try
        {
            await CreateProjectAsync(editorPath, projectPath, testRoot);
            CompanionInstaller installer = new(packagePath);
            installer.Install(projectPath);

            activeEditor = StartEditor(editorPath, projectPath, Path.Combine(testRoot, "first-editor.log"));
            AppLogger logger = new(logDirectory);
            WindowService windowService = new();
            EditorDiscoveryService discovery = new(windowService, logger);
            CompanionClient companion = new(installer);
            EditorInstance original = await WaitForReadyInstanceAsync(
                discovery,
                companion,
                projectPath,
                TimeSpan.FromMinutes(4));
            await Task.Delay(TimeSpan.FromSeconds(10));
            original = await WaitForReadyInstanceAsync(
                discovery,
                companion,
                projectPath,
                TimeSpan.FromSeconds(30));
            WindowSnapshot originalWindow = windowService.Capture(
                original.MainWindowHandle,
                original.WindowOrder) ?? throw new InvalidOperationException("无法捕获测试窗口。");

            RestartOrchestrator orchestrator = new(
                windowService,
                companion,
                new WindowTitleRenamerClient(logger),
                logger);
            using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(8));
            RestartBatchResult result = await orchestrator.RestartAsync(
                [original],
                RestartTrigger.Manual,
                timeout.Token);

            RestartInstanceResult instanceResult = Assert.Single(result.Instances);
            Assert.True(instanceResult.Succeeded, instanceResult.Message);
            EditorInstance restarted = await WaitForReadyInstanceAsync(
                discovery,
                companion,
                projectPath,
                TimeSpan.FromMinutes(2));
            activeEditor.Dispose();
            activeEditor = Process.GetProcessById(restarted.ProcessId);
            Assert.NotEqual(original.ProcessId, restarted.ProcessId);

            WindowSnapshot restoredWindow = windowService.Capture(
                restarted.MainWindowHandle,
                restarted.WindowOrder) ?? throw new InvalidOperationException("无法捕获重启后的窗口。");
            Assert.Equal(originalWindow.Placement.ShowCommand, restoredWindow.Placement.ShowCommand);
            AssertRectClose(originalWindow.Placement.NormalPosition, restoredWindow.Placement.NormalPosition, 3);

            PreflightResult cleanupPreflight = await companion.PreflightAsync(restarted, timeout.Token);
            Assert.True(cleanupPreflight.Eligible, cleanupPreflight.Reason);
            await companion.ShutdownAsync(restarted, timeout.Token);
            Assert.True(await WaitForExitAsync(restarted.ProcessId, TimeSpan.FromMinutes(2)));
            activeEditor.Dispose();
            activeEditor = null;
        }
        finally
        {
            if (activeEditor is not null)
            {
                TryTerminateDisposableEditor(activeEditor);
                activeEditor.Dispose();
            }
            DeleteDirectoryWithRetries(testRoot);
        }
    }

    private static string RequireEnvironmentPath(string name, Func<string, bool> exists)
    {
        string value = Environment.GetEnvironmentVariable(name) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !exists(value))
        {
            throw new InvalidOperationException($"环境变量 {name} 未指向有效路径。");
        }
        return Path.GetFullPath(value);
    }

    private static async Task CreateProjectAsync(string editorPath, string projectPath, string testRoot)
    {
        ProcessStartInfo startInfo = new(editorPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(editorPath)!,
        };
        startInfo.ArgumentList.Add("-batchmode");
        startInfo.ArgumentList.Add("-nographics");
        startInfo.ArgumentList.Add("-quit");
        startInfo.ArgumentList.Add("-createProject");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-logFile");
        startInfo.ArgumentList.Add(Path.Combine(testRoot, "create-project.log"));
        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException("无法启动编辑器创建测试项目。");
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(4));
        await process.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, process.ExitCode);
    }

    private static Process StartEditor(string editorPath, string projectPath, string logPath)
    {
        ProcessStartInfo startInfo = new(editorPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(editorPath)!,
        };
        startInfo.ArgumentList.Add("-projectPath");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-logFile");
        startInfo.ArgumentList.Add(logPath);
        return Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动测试编辑器。");
    }

    private static async Task<EditorInstance> WaitForReadyInstanceAsync(
        EditorDiscoveryService discovery,
        CompanionClient companion,
        string projectPath,
        TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            EditorInstance? instance = discovery.Discover().FirstOrDefault(candidate =>
                string.Equals(candidate.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase));
            if (instance is not null && companion.GetState(instance).Health == CompanionHealth.Ready)
            {
                return instance;
            }
            await Task.Delay(500);
        }
        throw new TimeoutException("等待测试编辑器及 companion 就绪超时。");
    }

    private static void AssertRectClose(
        Interop.NativeRect expected,
        Interop.NativeRect actual,
        int tolerance)
    {
        Assert.InRange(actual.Left, expected.Left - tolerance, expected.Left + tolerance);
        Assert.InRange(actual.Top, expected.Top - tolerance, expected.Top + tolerance);
        Assert.InRange(actual.Right, expected.Right - tolerance, expected.Right + tolerance);
        Assert.InRange(actual.Bottom, expected.Bottom - tolerance, expected.Bottom + tolerance);
    }

    private static async Task<bool> WaitForExitAsync(int processId, TimeSpan timeout)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            using CancellationTokenSource cancellation = new(timeout);
            await process.WaitForExitAsync(cancellation.Token);
            return true;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static void TryTerminateDisposableEditor(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                process.WaitForExit(30000);
            }
        }
        catch
        {
        }
    }

    private static void DeleteDirectoryWithRetries(string path)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(500);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(500);
            }
        }
    }
}
