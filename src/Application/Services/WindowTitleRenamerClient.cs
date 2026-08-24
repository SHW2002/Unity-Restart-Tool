using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityRestartTool.Infrastructure;

namespace UnityRestartTool.Services;

internal sealed class WindowTitleRenamerClient
{
    internal const string PipeName = "WindowTitleRenamer.UnityRestart.v1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private readonly AppLogger _logger;

    public WindowTitleRenamerClient(AppLogger logger)
    {
        _logger = logger;
    }

    public async Task<PersistentTitleRule?> QueryRuleAsync(
        IntPtr windowHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            PipeResponse response = await ExchangeAsync(
                new PipeRequest("query_persistent_rule", windowHandle.ToInt64(), null),
                cancellationToken);
            return response.Ok && response.HasRule && !string.IsNullOrWhiteSpace(response.Title)
                ? new PersistentTitleRule(response.Title)
                : null;
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
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions).AsMemory(), timeout.Token);
        string? responseJson = await reader.ReadLineAsync(timeout.Token);
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            throw new IOException("标题工具返回了空响应。");
        }

        return JsonSerializer.Deserialize<PipeResponse>(responseJson, JsonOptions) ??
            throw new IOException("标题工具返回了无效响应。");
    }

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
    }
}

internal sealed record PersistentTitleRule(string Title);
