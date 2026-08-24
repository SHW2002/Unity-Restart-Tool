using System;
using System.Collections.Generic;
using UnityEditor;

namespace UnityRestartCompanion
{
    internal static class EditorSafetyInspector
    {
        public static SafetySnapshot Capture()
        {
            SafetySnapshot snapshot = new SafetySnapshot();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                snapshot.busyReasons.Add("正在播放或切换 Play Mode");
            }
            if (EditorApplication.isCompiling)
            {
                snapshot.busyReasons.Add("正在编译脚本");
            }
            if (EditorApplication.isUpdating)
            {
                snapshot.busyReasons.Add("正在导入或刷新资源");
            }
            if (BuildPipeline.isBuildingPlayer)
            {
                snapshot.busyReasons.Add("正在构建 Player");
            }

            return snapshot;
        }
    }

    [Serializable]
    internal sealed class SafetySnapshot
    {
        public List<string> dirtyScenes = new List<string>();
        public List<string> dirtyAssets = new List<string>();
        public List<string> busyReasons = new List<string>();

        public bool IsSafe
        {
            get
            {
                return busyReasons.Count == 0;
            }
        }
    }
}
