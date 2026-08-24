using System.IO.Pipes;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityRestartTool.Infrastructure;
using UnityRestartTool.Models;

namespace UnityRestartTool.Services;

internal sealed class WindowTitleRenamerClient
{
    internal const string PipeName = "WindowTitleRenamer.UnityRestart.v1";
    internal const string ProcessName = "Window-Title-Renamer";
    internal static readonly Version MinimumSupportedVersion = new(2, 0, 0);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private readonly AppLogger _logger;

    public WindowTitleRenamerClient(AppLogger logger)
    {
        _logger = logger;
    }

    public async Task<WindowTitleRenamerStatus> CheckStatusAsync(
        CancellationToken cancellationToken)
    {
        DetectedProcess detected = FindRunningProcess();
        if (detected.Version is not null && !IsSupportedVersion(detected.Version))
        {
            return new WindowTitleRenamerStatus(
                WindowTitleRenamerHealth.Incompatible,
                $"版本过低（{FormatVersion(detected.Version)}，需要 {MinimumSupportedVersion}+）",
                detected.Version);
        }

        try
        {
            PipeResponse response = await ExchangeAsync(
                new PipeRequest("query_persistent_rule", 1, null),
                cancellationToken);
            if (!response.Ok)
            {
                return new WindowTitleRenamerStatus(
                    WindowTitleRenamerHealth.Incompatible,
                    $"协议不兼容: {response.Error ?? "探测请求被拒绝"}",
                    detected.Version);
            }

            string message = detected.Version is null
                ? "就绪（协议 v1）"
                : $"就绪（{FormatVersion(detected.Version)}）";
            return new WindowTitleRenamerStatus(
                WindowTitleRenamerHealth.Ready,
                message,
                detected.Version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is TimeoutException or IOException or JsonException)
        {
            string version = detected.Version is null
                ? string.Empty
                : $"（{FormatVersion(detected.Version)}）";
            return detected.IsRunning
                ? new WindowTitleRenamerStatus(
                    WindowTitleRenamerHealth.Unavailable,
                    $"通信不可用{version}: {exception.Message}",
                    detected.Version)
                : new WindowTitleRenamerStatus(
                    WindowTitleRenamerHealth.NotRunning,
                    "未运行，无法检测版本");
        }
    }

    public async Task<PersistentTitleRule?> QueryRestoreRuleAsync(
        IntPtr windowHandle,
        string currentWindowTitle,
        CancellationToken cancellationToken)
    {
        try
        {
            PipeResponse response = await ExchangeAsync(
                new PipeRequest("query_persistent_rule", windowHandle.ToInt64(), null),
                cancellationToken);
            if (!response.Ok)
            {
                _logger.Warning(
                    "标题联动",
                    $"查询标题规则被拒绝: {response.Error ?? "未知协议错误"}");
                return null;
            }
            PersistentTitleRule? restoreRule = SelectRestoreRule(
                response.HasRule,
                response.Title,
                currentWindowTitle);
            if (!response.HasRule && restoreRule is not null)
            {
                _logger.Info("标题联动", "未找到持久标题规则，将使用当前窗口标题恢复");
            }
            return restoreRule;
        }
        catch (Exception exception) when (
            exception is TimeoutException or IOException or OperationCanceledException)
        {
            if (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.Warning("标题联动", $"Window-Title-Renamer 不可用: {exception.Message}");
            }

            return null;
        }
    }

    public async Task<bool> BindRuleAsync(
        IntPtr windowHandle,
        PersistentTitleRule rule,
        CancellationToken cancellationToken)
    {
        try
        {
            PipeResponse response = await ExchangeAsync(
                new PipeRequest("bind_persistent_rule", windowHandle.ToInt64(), rule.Title),
                cancellationToken);
            if (!response.Ok)
            {
                _logger.Warning(
                    "标题联动",
                    $"恢复标题规则被拒绝: {response.Error ?? "未知协议错误"}");
            }
            return response.Ok;
        }
        catch (Exception exception) when (
            exception is TimeoutException or IOException or OperationCanceledException)
        {
            if (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.Warning("标题联动", $"无法恢复持续标题规则: {exception.Message}");
            }

            return false;
        }
    }

    private static async Task<PipeResponse> ExchangeAsync(
        PipeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            await using NamedPipeClientStream pipe = new(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token);

            using StreamWriter writer = new(pipe, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true,
            };
            using StreamReader reader = new(pipe, Encoding.UTF8, leaveOpen: true);
            await writer.WriteLineAsync(
                JsonSerializer.Serialize(request, JsonOptions).AsMemory(),
                timeout.Token);
            string? responseJson = await reader.ReadLineAsync(timeout.Token);
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                throw new IOException("标题工具返回了空响应。");
            }

            return JsonSerializer.Deserialize<PipeResponse>(responseJson, JsonOptions) ??
                throw new IOException("标题工具返回了无效响应。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("标题工具连接超时。");
        }
    }

    internal static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string numeric = new string(value.Trim()
            .TakeWhile(character => char.IsAsciiDigit(character) || character == '.')
            .ToArray()).TrimEnd('.');
        return Version.TryParse(numeric, out Version? version) ? version : null;
    }

    internal static bool IsSupportedVersion(Version version) =>
        version >= MinimumSupportedVersion;

    internal static PersistentTitleRule? SelectRestoreRule(
        bool hasPersistentRule,
        string? persistentTitle,
        string? currentWindowTitle)
    {
        string? title = hasPersistentRule && !string.IsNullOrWhiteSpace(persistentTitle)
            ? persistentTitle
            : currentWindowTitle;
        return string.IsNullOrWhiteSpace(title) ? null : new PersistentTitleRule(title);
    }

    private static DetectedProcess FindRunningProcess()
    {
        bool isRunning = false;
        Version? newestVersion = null;
        foreach (Process process in Process.GetProcessesByName(ProcessName))
        {
            using (process)
            {
                isRunning = true;
                try
                {
                    Version? version = ParseVersion(process.MainModule?.FileVersionInfo.FileVersion);
                    if (version is not null && (newestVersion is null || version > newestVersion))
                    {
                        newestVersion = version;
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
                {
                }
            }
        }

        return new DetectedProcess(isRunning, newestVersion);
    }

    private static string FormatVersion(Version version) =>
        version.Build >= 0 ? version.ToString(3) : version.ToString();

    private readonly record struct DetectedProcess(bool IsRunning, Version? Version);

    private sealed record PipeRequest(
        [property: JsonPropertyName("operation")] string Operation,
        [property: JsonPropertyName("hwnd")] long WindowHandle,
        [property: JsonPropertyName("title")] string? Title);

    private sealed class PipeResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("hasRule")]
        public bool HasRule { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}

internal sealed record PersistentTitleRule(string Title);
