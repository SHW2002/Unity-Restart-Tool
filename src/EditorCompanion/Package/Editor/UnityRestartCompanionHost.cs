using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityRestartCompanion
{
    [InitializeOnLoad]
    internal static class UnityRestartCompanionHost
    {
        private const int ProtocolVersion = 1;
        private const string CompanionVersion = "1.0.1";
        private const double PollIntervalSeconds = 0.2;
        private const double HeartbeatIntervalSeconds = 1.0;
        private const double ConsoleStabilizationSeconds = 3.0;
        private static readonly string RootPath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            "Library",
            "UnityRestartTool");
        private static readonly string RequestPath = Path.Combine(RootPath, "request.json");
        private static readonly string ProcessingPath = Path.Combine(RootPath, "processing.json");
        private static readonly string ResponsePath = Path.Combine(RootPath, "response.json");
        private static readonly string StatusPath = Path.Combine(RootPath, "status.json");
        private static double s_NextPoll;
        private static double s_NextHeartbeat;
        private static PendingPreflight s_PendingPreflight;

        static UnityRestartCompanionHost()
        {
            Directory.CreateDirectory(RootPath);
            RecoverInterruptedRequest();
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            WriteStatus();
        }

        private static void Update()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now >= s_NextHeartbeat)
            {
                s_NextHeartbeat = now + HeartbeatIntervalSeconds;
                WriteStatus();
            }

            if (s_PendingPreflight != null && now >= s_PendingPreflight.CompleteAt)
            {
                CompletePreflight(s_PendingPreflight);
                s_PendingPreflight = null;
                return;
            }

            if (s_PendingPreflight == null && now >= s_NextPoll)
            {
                s_NextPoll = now + PollIntervalSeconds;
                TryClaimAndProcess();
            }
        }

        private static void TryClaimAndProcess()
        {
            if (File.Exists(ResponsePath) || File.Exists(ProcessingPath) || !File.Exists(RequestPath))
            {
                return;
            }

            string requestId = string.Empty;
            try
            {
                File.Move(RequestPath, ProcessingPath);
                RequestEnvelope request = JsonUtility.FromJson<RequestEnvelope>(File.ReadAllText(ProcessingPath));
                requestId = request == null ? string.Empty : request.id;
                if (request == null || request.v != ProtocolVersion || string.IsNullOrEmpty(request.id))
                {
                    PublishError(request == null ? string.Empty : request.id, "INVALID_REQUEST", "请求格式或协议版本无效");
                    return;
                }

                if (request.command == "preflight")
                {
                    BeginPreflight(request);
                    return;
                }
                if (request.command == "shutdown")
                {
                    HandleShutdown(request);
                    return;
                }

                PublishError(request.id, "UNKNOWN_COMMAND", "未知 companion 命令");
            }
            catch (Exception exception)
            {
                PublishError(requestId, "PROTOCOL_ERROR", exception.Message);
            }
        }

        private static void BeginPreflight(RequestEnvelope request)
        {
            SafetySnapshot initial = EditorSafetyInspector.Capture(true);
            if (!initial.IsSafe)
            {
                PublishPreflight(request.id, BuildPayload(initial, 0, "编辑器存在忙碌或未保存状态"));
                return;
            }

            int clearedCount;
            string error;
            if (!ConsoleAccess.TryClear(out clearedCount, out error))
            {
                PublishError(request.id, "CONSOLE_UNAVAILABLE", error);
                return;
            }

            s_PendingPreflight = new PendingPreflight(
                request.id,
                EditorApplication.timeSinceStartup + ConsoleStabilizationSeconds);
        }

        private static void CompletePreflight(PendingPreflight pending)
        {
            SafetySnapshot safety = EditorSafetyInspector.Capture(true);
            int consoleCount;
            string error;
            if (!ConsoleAccess.TryGetCount(out consoleCount, out error))
            {
                PublishError(pending.RequestId, "CONSOLE_UNAVAILABLE", error);
                return;
            }

            string reason = consoleCount > 0
                ? "Console 清空后重新出现信息"
                : safety.IsSafe ? string.Empty : "观察期间出现忙碌或未保存状态";
            PublishPreflight(pending.RequestId, BuildPayload(safety, consoleCount, reason));
        }

        private static void HandleShutdown(RequestEnvelope request)
        {
            SafetySnapshot safety = EditorSafetyInspector.Capture(true);
            int consoleCount;
            string error;
            if (!ConsoleAccess.TryGetCount(out consoleCount, out error))
            {
                PublishError(request.id, "CONSOLE_UNAVAILABLE", error);
                return;
            }

            PreflightPayload payload = BuildPayload(
                safety,
                consoleCount,
                consoleCount > 0 ? "退出前 Console 出现新信息" :
                    safety.IsSafe ? string.Empty : "退出前编辑器状态发生变化");
            if (!payload.eligible)
            {
                PublishError(request.id, "SHUTDOWN_REJECTED", payload.reason);
                return;
            }

            PublishPreflight(request.id, payload);
            EditorApplication.update -= ExitAfterResponse;
            EditorApplication.update += ExitAfterResponse;
        }

        private static void ExitAfterResponse()
        {
            EditorApplication.update -= ExitAfterResponse;
            EditorApplication.Exit(0);
        }

        private static PreflightPayload BuildPayload(
            SafetySnapshot safety,
            int consoleCount,
            string reason)
        {
            return new PreflightPayload
            {
                eligible = safety.IsSafe && consoleCount == 0,
                reason = reason,
                consoleEntryCount = consoleCount,
                dirtyScenes = safety.dirtyScenes,
                dirtyAssets = safety.dirtyAssets,
                busyReasons = safety.busyReasons
            };
        }

        private static void PublishPreflight(string id, PreflightPayload payload)
        {
            AtomicJsonFile.Write(ResponsePath, new ResponseEnvelope
            {
                id = id,
                status = "ok",
                result = payload
            });
            AtomicJsonFile.DeleteBestEffort(ProcessingPath);
        }

        private static void PublishError(string id, string code, string message)
        {
            AtomicJsonFile.Write(ResponsePath, new ResponseEnvelope
            {
                id = id ?? string.Empty,
                status = "error",
                error = new ErrorPayload { code = code, message = message }
            });
            AtomicJsonFile.DeleteBestEffort(ProcessingPath);
        }

        private static void WriteStatus()
        {
            try
            {
                SafetySnapshot safety = EditorSafetyInspector.Capture(false);
                AtomicJsonFile.Write(StatusPath, new StatusEnvelope
                {
                    companionVersion = CompanionVersion,
                    processId = System.Diagnostics.Process.GetCurrentProcess().Id,
                    heartbeatUtc = DateTime.UtcNow.ToString("o"),
                    editorVersion = Application.unityVersion,
                    projectPath = Directory.GetParent(Application.dataPath).FullName,
                    busyReasons = safety.busyReasons
                });
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[UnityRestartCompanion] 写入心跳失败: " + exception.Message);
            }
        }

        private static void RecoverInterruptedRequest()
        {
            if (!File.Exists(ProcessingPath))
            {
                return;
            }

            if (File.Exists(ResponsePath))
            {
                AtomicJsonFile.DeleteBestEffort(ProcessingPath);
                return;
            }

            string id = string.Empty;
            try
            {
                RequestEnvelope request = JsonUtility.FromJson<RequestEnvelope>(File.ReadAllText(ProcessingPath));
                id = request == null ? string.Empty : request.id;
            }
            catch (Exception)
            {
            }
            PublishError(id, "INTERRUPTED", "请求被编辑器 Domain Reload 中断");
        }

        private sealed class PendingPreflight
        {
            public readonly string RequestId;
            public readonly double CompleteAt;

            public PendingPreflight(string requestId, double completeAt)
            {
                RequestId = requestId;
                CompleteAt = completeAt;
            }
        }
    }
}
