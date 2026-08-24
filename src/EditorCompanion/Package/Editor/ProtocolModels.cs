using System;
using System.Collections.Generic;

namespace UnityRestartCompanion
{
    [Serializable]
    internal sealed class RequestEnvelope
    {
        public int v;
        public string id;
        public string command;
    }

    [Serializable]
    internal sealed class ResponseEnvelope
    {
        public int v = 1;
        public string id;
        public string status;
        public PreflightPayload result;
        public ErrorPayload error;
    }

    [Serializable]
    internal sealed class ErrorPayload
    {
        public string code;
        public string message;
    }

    [Serializable]
    internal sealed class PreflightPayload
    {
        public bool eligible;
        public string reason;
        public int consoleEntryCount;
        public List<string> dirtyScenes = new List<string>();
        public List<string> dirtyAssets = new List<string>();
        public List<string> busyReasons = new List<string>();
    }

    [Serializable]
    internal sealed class StatusEnvelope
    {
        public int protocolVersion = 1;
        public int processId;
        public string heartbeatUtc;
        public string editorVersion;
        public string projectPath;
        public List<string> busyReasons = new List<string>();
    }
}
