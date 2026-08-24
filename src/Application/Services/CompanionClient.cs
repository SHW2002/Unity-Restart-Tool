using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityRestartTool.Models;

namespace UnityRestartTool.Services;

internal sealed class CompanionClient
{
    internal const int ProtocolVersion = 1;
    internal static readonly Version MinimumCompanionVersion = new(1, 0, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _projectLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly CompanionInstaller _installer;

    public CompanionClient(CompanionInstaller installer)
    {
        _installer = installer;
    }

    public CompanionState GetState(EditorInstance instance)
    {
        CompanionInstallInfo install = _installer.Inspect(instance.ProjectPath);
        if (!install.Installed)
        {
            return new CompanionState(
                install.HasConflict ? CompanionHealth.Error : CompanionHealth.NotInstalled,
                install.Message);
        }

        string statusPath = Path.Combine(GetRoot(instance.ProjectPath), "status.json");
        if (!File.Exists(statusPath))
        {
            return new CompanionState(CompanionHealth.Starting, "等待 companion 启动");
        }

        try
        {
            CompanionStatus? status = JsonSerializer.Deserialize<CompanionStatus>(
                File.ReadAllText(statusPath), JsonOptions);
            if (status is null || status.ProtocolVersion != ProtocolVersion)
            {
                return new CompanionState(
                    CompanionHealth.Incompatible,
                    "companion 协议版本不兼容",
                    status?.ProcessId,
                    status?.ProtocolVersion,
                    status?.HeartbeatUtc);
            }

            if (status.ProcessId != instance.ProcessId)
            {
                return new CompanionState(
                    CompanionHealth.Stale,
                    "状态文件属于旧编辑器进程",
                    status.ProcessId,
                    status.ProtocolVersion,
                    status.HeartbeatUtc);
            }

            if (DateTime.UtcNow - status.HeartbeatUtc > TimeSpan.FromSeconds(5))
            {
                return new CompanionState(
                    CompanionHealth.Stale,
                    "companion 心跳已过期",
                    status.ProcessId,
                    status.ProtocolVersion,
                    status.HeartbeatUtc);
            }

            if (!IsSupportedCompanionVersion(status.CompanionVersion))
            {
                string currentVersion = string.IsNullOrWhiteSpace(status.CompanionVersion)
                    ? "旧版"
                    : status.CompanionVersion;
                return new CompanionState(
                    CompanionHealth.Incompatible,
                    $"companion 版本过低（当前 {currentVersion}，要求 {MinimumCompanionVersion} 或更高），" +
                    "请安装 / 升级后刷新编辑器",
                    status.ProcessId,
                    status.ProtocolVersion,
                    status.HeartbeatUtc);
            }

            return new CompanionState(
                CompanionHealth.Ready,
                "就绪",
                status.ProcessId,
                status.ProtocolVersion,
                status.HeartbeatUtc);
        }
        catch (Exception exception)
        {
            return new CompanionState(CompanionHealth.Error, $"状态读取失败: {exception.Message}");
        }
    }

    internal static bool IsSupportedCompanionVersion(string? version) =>
        Version.TryParse(version, out Version? parsed) && parsed >= MinimumCompanionVersion;

    public async Task<PreflightResult> PreflightAsync(
        EditorInstance instance,
        CancellationToken cancellationToken)
    {
        CompanionResponse response = await SendAsync(
            instance.ProjectPath,
            "preflight",
            TimeSpan.FromMinutes(2),
            cancellationToken);
        if (!response.Ok)
        {
            return new PreflightResult(false, response.Error ?? "预检失败", 0, [], [], []);
        }

        CompanionPreflight payload = response.Result.Deserialize<CompanionPreflight>(JsonOptions) ??
            throw new InvalidDataException("companion 预检响应缺少结果。");
        return new PreflightResult(
            payload.Eligible,
            payload.Reason ?? string.Empty,
            payload.ConsoleEntryCount,
            payload.DirtyScenes ?? [],
            payload.DirtyAssets ?? [],
            payload.BusyReasons ?? []);
    }

    public async Task ShutdownAsync(
        EditorInstance instance,
        CancellationToken cancellationToken)
    {
        CompanionResponse response = await SendAsync(
            instance.ProjectPath,
            "shutdown",
            TimeSpan.FromMinutes(2),
            cancellationToken);
        if (!response.Ok)
        {
            throw new InvalidOperationException(response.Error ?? "companion 拒绝退出请求。");
        }

        CompanionPreflight payload = response.Result.Deserialize<CompanionPreflight>(JsonOptions) ??
            throw new InvalidDataException("companion 退出响应缺少结果。");
        if (!payload.Eligible)
        {
            string reason = string.IsNullOrWhiteSpace(payload.Reason)
                ? "退出前安全状态发生变化"
                : payload.Reason;
            throw new InvalidOperationException(reason);
        }
    }

    private async Task<CompanionResponse> SendAsync(
        string projectPath,
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim projectLock = _projectLocks.GetOrAdd(projectPath, _ => new SemaphoreSlim(1, 1));
        await projectLock.WaitAsync(cancellationToken);
        try
        {
            string root = GetRoot(projectPath);
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException("companion 通信目录不存在。");
            }

            string requestPath = Path.Combine(root, "request.json");
            string processingPath = Path.Combine(root, "processing.json");
            string responsePath = Path.Combine(root, "response.json");
            CleanupStaleExchange(requestPath, processingPath, responsePath);
            if (File.Exists(requestPath) || File.Exists(processingPath) || File.Exists(responsePath))
            {
                throw new InvalidOperationException("companion 正在处理另一条请求。");
            }

            string id = Guid.NewGuid().ToString("N");
            string temporaryPath = requestPath + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(new CompanionRequest(ProtocolVersion, id, command), JsonOptions));
            File.Move(temporaryPath, requestPath);

            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(responsePath))
                {
                    string json;
                    try
                    {
                        json = File.ReadAllText(responsePath);
                    }
                    catch (IOException)
                    {
                        await Task.Delay(100, cancellationToken);
                        continue;
                    }

                    CompanionResponseEnvelope? envelope =
                        JsonSerializer.Deserialize<CompanionResponseEnvelope>(json, JsonOptions);
                    if (envelope is not null && string.Equals(envelope.Id, id, StringComparison.Ordinal))
                    {
                        File.Delete(responsePath);
                        return envelope.Status == "ok"
                            ? new CompanionResponse(true, envelope.Result, null)
                            : new CompanionResponse(
                                false,
                                default,
                                envelope.Error?.Message ?? envelope.Error?.Code ?? "未知 companion 错误");
                    }
                }

                await Task.Delay(100, cancellationToken);
            }

            throw new TimeoutException($"companion 命令 {command} 响应超时。");
        }
        finally
        {
            projectLock.Release();
        }
    }

    private static void CleanupStaleExchange(
        string requestPath,
        string processingPath,
        string responsePath)
    {
        DateTime cutoff = DateTime.UtcNow.AddMinutes(-2);
        string[] paths = [requestPath, processingPath, responsePath];
        if (paths.Where(File.Exists).Any(path => File.GetLastWriteTimeUtc(path) >= cutoff))
        {
            return;
        }

        foreach (string path in paths)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static string GetRoot(string projectPath) =>
        Path.Combine(projectPath, "Library", "UnityRestartTool");

    private sealed record CompanionRequest(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("command")] string Command);

    private sealed class CompanionStatus
    {
        [JsonPropertyName("protocolVersion")]
        public int ProtocolVersion { get; set; }

        [JsonPropertyName("processId")]
        public int ProcessId { get; set; }

        [JsonPropertyName("companionVersion")]
        public string? CompanionVersion { get; set; }

        [JsonPropertyName("heartbeatUtc")]
        public DateTime HeartbeatUtc { get; set; }
    }

    private sealed class CompanionResponseEnvelope
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("result")]
        public JsonElement Result { get; set; }

        [JsonPropertyName("error")]
        public CompanionError? Error { get; set; }
    }

    private sealed class CompanionError
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    private sealed class CompanionPreflight
    {
        [JsonPropertyName("eligible")]
        public bool Eligible { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("consoleEntryCount")]
        public int ConsoleEntryCount { get; set; }

        [JsonPropertyName("dirtyScenes")]
        public string[]? DirtyScenes { get; set; }

        [JsonPropertyName("dirtyAssets")]
        public string[]? DirtyAssets { get; set; }

        [JsonPropertyName("busyReasons")]
        public string[]? BusyReasons { get; set; }
    }

    private readonly record struct CompanionResponse(bool Ok, JsonElement Result, string? Error);
}
