using System.Diagnostics;
using UnityRestartTool.Infrastructure;
using UnityRestartTool.Models;

namespace UnityRestartTool.Services;

internal sealed class RestartOrchestrator
{
    private static readonly TimeSpan CompanionExitGracePeriod = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromMinutes(10);
    private readonly WindowService _windowService;
    private readonly CompanionClient _companionClient;
    private readonly WindowTitleRenamerClient _titleClient;
    private readonly AppLogger _logger;
    private readonly SemaphoreSlim _batchLock = new(1, 1);

    public RestartOrchestrator(
        WindowService windowService,
        CompanionClient companionClient,
        WindowTitleRenamerClient titleClient,
        AppLogger logger)
    {
        _windowService = windowService;
        _companionClient = companionClient;
        _titleClient = titleClient;
        _logger = logger;
    }

    public event EventHandler<RestartProgress>? ProgressChanged;

    public bool IsRunning => _batchLock.CurrentCount == 0;

    public async Task<RestartBatchResult> RestartAsync(
        IReadOnlyList<EditorInstance> requestedInstances,
        RestartTrigger trigger,
        CancellationToken cancellationToken)
    {
        if (!await _batchLock.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("已有重启批次正在执行。");
        }

        DateTime startedAt = DateTime.Now;
        List<RestartInstanceResult> results = [];
        try
        {
            List<PreparedInstance> prepared = [];
            foreach (EditorInstance instance in requestedInstances)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PreparedInstance? candidate = await PrepareAsync(instance, trigger, results, cancellationToken);
                if (candidate is not null)
                {
                    prepared.Add(candidate);
                }
            }

            List<PreparedInstance> stopped = [];
            foreach (PreparedInstance candidate in prepared.OrderByDescending(value => value.Instance.StartTime))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report(candidate.Instance, RestartStage.Stopping, "正在请求编辑器安全退出");
                try
                {
                    await _companionClient.ShutdownAsync(candidate.Instance, cancellationToken);
                    bool exited = await WaitForExitAsync(
                        candidate.Instance.ProcessId,
                        CompanionExitGracePeriod,
                        cancellationToken);
                    if (!exited)
                    {
                        _logger.Warning(
                            candidate.Instance.ProjectName,
                            "companion 退出请求后进程仍在运行，发送标准 WM_CLOSE 补偿请求");
                        _windowService.RequestGracefulClose(candidate.Instance.MainWindowHandle);
                        exited = await WaitForExitAsync(
                            candidate.Instance.ProcessId,
                            ExitTimeout,
                            cancellationToken);
                    }

                    if (!exited)
                    {
                        string message = "编辑器在安全退出等待期内未结束，已保留进程且不会强制结束";
                        Report(candidate.Instance, RestartStage.Failed, message, true);
                        results.Add(new RestartInstanceResult(
                            candidate.Instance.ProjectPath, false, false, message));
                        continue;
                    }

                    stopped.Add(candidate);
                    _logger.Info(candidate.Instance.ProjectName, "编辑器已正常退出");
                }
                catch (Exception exception)
                {
                    string message = $"安全退出失败: {exception.Message}";
                    Report(candidate.Instance, RestartStage.Failed, message, true);
                    results.Add(new RestartInstanceResult(
                        candidate.Instance.ProjectPath, false, false, message));
                }
            }

            foreach (PreparedInstance candidate in stopped.OrderBy(value => value.Instance.StartTime))
            {
                cancellationToken.ThrowIfCancellationRequested();
                RestartInstanceResult result = await StartAndRestoreAsync(candidate, cancellationToken);
                results.Add(result);
            }

            return new RestartBatchResult(startedAt, DateTime.Now, results);
        }
        finally
        {
            _batchLock.Release();
        }
    }

    private async Task<PreparedInstance?> PrepareAsync(
        EditorInstance instance,
        RestartTrigger trigger,
        ICollection<RestartInstanceResult> results,
        CancellationToken cancellationToken)
    {
        Report(instance, RestartStage.Preflight, "正在执行重启前检查");
        if (!IsProcessAlive(instance.ProcessId))
        {
            return Skip(instance, "编辑器进程已经退出", results);
        }

        if (trigger == RestartTrigger.Scheduled && _windowService.IsForegroundProcess(instance.ProcessId))
        {
            return Skip(instance, "定时任务跳过当前前台编辑器", results);
        }

        if (_windowService.HasDisabledTopLevelWindow(instance.ProcessId))
        {
            return Skip(instance, "编辑器存在模态窗口或禁用窗口", results);
        }

        CompanionState state = _companionClient.GetState(instance);
        if (!state.CanRestart)
        {
            return Skip(instance, $"companion 不可用: {state.Message}", results);
        }

        PreflightResult preflight;
        try
        {
            preflight = await _companionClient.PreflightAsync(instance, cancellationToken);
        }
        catch (Exception exception)
        {
            return Skip(instance, $"companion 预检失败: {exception.Message}", results);
        }

        if (!preflight.Eligible)
        {
            string reason = BuildPreflightReason(preflight);
            return Skip(instance, reason, results);
        }

        IntPtr currentWindowHandle = _windowService.FindMainWindow(instance.ProcessId);
        if (currentWindowHandle == IntPtr.Zero)
        {
            return Skip(instance, "编辑器主窗口已经消失", results);
        }

        EditorInstance currentInstance = instance with
        {
            MainWindowHandle = currentWindowHandle,
            WindowTitle = Interop.NativeMethods.ReadWindowTitle(currentWindowHandle),
            WindowOrder = _windowService.GetWindowOrder(currentWindowHandle),
        };
        WindowSnapshot? snapshot;
        try
        {
            snapshot = _windowService.Capture(
                currentInstance.MainWindowHandle,
                currentInstance.WindowOrder);
        }
        catch (Exception exception)
        {
            return Skip(instance, $"无法读取窗口布局: {exception.Message}", results);
        }

        if (snapshot is null)
        {
            return Skip(instance, "编辑器主窗口已经消失", results);
        }

        PersistentTitleRule? titleRule = await _titleClient.QueryRestoreRuleAsync(
            currentInstance.MainWindowHandle,
            currentInstance.WindowTitle,
            cancellationToken);
        Report(currentInstance, RestartStage.Preflight, "预检通过");
        return new PreparedInstance(currentInstance, snapshot, titleRule);
    }

    private PreparedInstance? Skip(
        EditorInstance instance,
        string reason,
        ICollection<RestartInstanceResult> results)
    {
        Report(instance, RestartStage.Skipped, reason);
        results.Add(new RestartInstanceResult(instance.ProjectPath, false, true, reason));
        _logger.Warning(instance.ProjectName, reason);
        return null;
    }

    private async Task<RestartInstanceResult> StartAndRestoreAsync(
        PreparedInstance candidate,
        CancellationToken cancellationToken)
    {
        EditorInstance instance = candidate.Instance;
        Report(instance, RestartStage.Starting, "正在启动编辑器");
        try
        {
            LaunchedEditor launched = await LaunchWithRetryAsync(instance, cancellationToken);
            Report(instance, RestartStage.RestoringWindow, "正在恢复窗口位置与状态");
            _windowService.Restore(launched.MainWindowHandle, candidate.WindowSnapshot);

            bool titleRestored = true;
            if (candidate.TitleRule is not null)
            {
                Report(instance, RestartStage.RestoringTitle, "正在恢复持续标题规则");
                titleRestored = await _titleClient.BindRuleAsync(
                    launched.MainWindowHandle,
                    candidate.TitleRule,
                    cancellationToken);
            }

            string message = candidate.TitleRule is null
                ? "重启完成，窗口布局已恢复，但未发现可恢复的标题规则"
                : titleRestored
                    ? "重启完成，窗口与标题规则已恢复"
                    : "重启完成，但持续标题规则恢复失败";
            bool titleRecoveryWarning = candidate.TitleRule is null || !titleRestored;
            Report(instance, RestartStage.Completed, message, titleRecoveryWarning);
            if (titleRecoveryWarning)
            {
                _logger.Warning(instance.ProjectName, message);
            }
            else
            {
                _logger.Info(instance.ProjectName, message);
            }
            return new RestartInstanceResult(instance.ProjectPath, true, false, message);
        }
        catch (Exception exception)
        {
            string message = $"重新启动失败: {exception.Message}";
            Report(instance, RestartStage.Failed, message, true);
            _logger.Error(instance.ProjectName, message, exception);
            return new RestartInstanceResult(instance.ProjectPath, false, false, message);
        }
    }

    private async Task<LaunchedEditor> LaunchWithRetryAsync(
        EditorInstance instance,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            Process? process = null;
            try
            {
                ProcessStartInfo startInfo = new(instance.ExecutablePath)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(instance.ExecutablePath) ?? string.Empty,
                };
                startInfo.ArgumentList.Add("-projectPath");
                startInfo.ArgumentList.Add(instance.ProjectPath);
                process = Process.Start(startInfo) ?? throw new InvalidOperationException("Process.Start 未返回进程。");
                IntPtr mainWindow = await WaitForMainWindowAsync(process, instance, cancellationToken);
                return new LaunchedEditor(process.Id, mainWindow);
            }
            catch (Exception exception)
            {
                lastFailure = exception;
                bool mayRetry = process is null || HasExited(process);
                process?.Dispose();
                if (!mayRetry || attempt == 2)
                {
                    break;
                }

                _logger.Warning(instance.ProjectName, $"首次启动失败，准备重试: {exception.Message}");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        throw new InvalidOperationException("启动重试失败。", lastFailure);
    }

    private async Task<IntPtr> WaitForMainWindowAsync(
        Process process,
        EditorInstance original,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + LaunchTimeout;
        IntPtr lastCandidate = IntPtr.Zero;
        int stableSamples = 0;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasExited(process))
            {
                throw new InvalidOperationException($"编辑器启动后立即退出，退出码 {process.ExitCode}。");
            }

            IntPtr candidate = _windowService.FindMainWindow(process.Id);
            if (candidate != IntPtr.Zero && candidate == lastCandidate)
            {
                stableSamples++;
            }
            else
            {
                lastCandidate = candidate;
                stableSamples = candidate == IntPtr.Zero ? 0 : 1;
            }

            if (candidate != IntPtr.Zero && stableSamples >= 4)
            {
                EditorInstance launchedInstance = original with
                {
                    ProcessId = process.Id,
                    MainWindowHandle = candidate,
                    StartTime = process.StartTime,
                };
                CompanionState state = _companionClient.GetState(launchedInstance);
                if (state.Health == CompanionHealth.Ready)
                {
                    return candidate;
                }
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException("等待新编辑器主窗口超时，现有进程不会被强制结束。");
    }

    private static async Task<bool> WaitForExitAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(deadline.Token);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private static string BuildPreflightReason(PreflightResult preflight)
    {
        List<string> reasons = [];
        if (!string.IsNullOrWhiteSpace(preflight.Reason))
        {
            reasons.Add(preflight.Reason);
        }
        if (preflight.ConsoleEntryCount > 0)
        {
            reasons.Add($"Console 清空后仍有 {preflight.ConsoleEntryCount} 条信息");
        }
        if (preflight.DirtyScenes.Count > 0)
        {
            reasons.Add($"未保存场景: {string.Join(", ", preflight.DirtyScenes.Take(3))}");
        }
        if (preflight.DirtyAssets.Count > 0)
        {
            reasons.Add($"未保存资源: {string.Join(", ", preflight.DirtyAssets.Take(3))}");
        }
        if (preflight.BusyReasons.Count > 0)
        {
            reasons.Add($"编辑器忙碌: {string.Join(", ", preflight.BusyReasons)}");
        }

        return reasons.Count > 0 ? string.Join("；", reasons) : "companion 拒绝重启";
    }

    private void Report(EditorInstance instance, RestartStage stage, string message, bool isError = false)
    {
        ProgressChanged?.Invoke(this, new RestartProgress(instance.ProjectPath, stage, message, isError));
    }

    private sealed record PreparedInstance(
        EditorInstance Instance,
        WindowSnapshot WindowSnapshot,
        PersistentTitleRule? TitleRule);

    private sealed record LaunchedEditor(int ProcessId, IntPtr MainWindowHandle);
}
