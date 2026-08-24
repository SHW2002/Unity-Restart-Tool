using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityRestartCompanion
{
    internal static class EditorSafetyInspector
    {
        public static SafetySnapshot Capture(bool includeDirtyObjects)
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

            if (!includeDirtyObjects)
            {
                return snapshot;
            }

            if (snapshot.busyReasons.Count > 0)
            {
                return snapshot;
            }

            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.IsValid() && scene.isDirty)
                {
                    snapshot.dirtyScenes.Add(SceneLabel(scene));
                }
            }

            Scene? prefabScene = TryGetPrefabStageScene();
            if (prefabScene.HasValue && prefabScene.Value.IsValid() && prefabScene.Value.isDirty)
            {
                string label = SceneLabel(prefabScene.Value);
                if (!snapshot.dirtyScenes.Contains(label))
                {
                    snapshot.dirtyScenes.Add(label);
                }
            }

            Dictionary<string, List<UnityEngine.Object>> dirtyObjectsByAsset =
                new Dictionary<string, List<UnityEngine.Object>>(StringComparer.OrdinalIgnoreCase);
            UnityEngine.Object[] loadedObjects = Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
            foreach (UnityEngine.Object loadedObject in loadedObjects)
            {
                if (loadedObject == null ||
                    !EditorUtility.IsPersistent(loadedObject) ||
                    !EditorUtility.IsDirty(loadedObject))
                {
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(loadedObject);
                if (IsUserProjectAsset(path))
                {
                    List<UnityEngine.Object> dirtyObjects;
                    if (!dirtyObjectsByAsset.TryGetValue(path, out dirtyObjects))
                    {
                        dirtyObjects = new List<UnityEngine.Object>();
                        dirtyObjectsByAsset.Add(path, dirtyObjects);
                    }
                    dirtyObjects.Add(loadedObject);
                }
            }

            foreach (KeyValuePair<string, List<UnityEngine.Object>> entry in dirtyObjectsByAsset)
            {
                if (!DirtyAssetContentComparer.TryClearFalsePositive(entry.Key, entry.Value))
                {
                    snapshot.dirtyAssets.Add(entry.Key);
                }
            }

            snapshot.dirtyScenes.Sort(StringComparer.OrdinalIgnoreCase);
            snapshot.dirtyAssets.Sort(StringComparer.OrdinalIgnoreCase);
            return snapshot;
        }

        private static bool IsUserProjectAsset(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith("ProjectSettings/", StringComparison.OrdinalIgnoreCase));
        }

        private static string SceneLabel(Scene scene)
        {
            if (!string.IsNullOrEmpty(scene.path))
            {
                return scene.path;
            }
            return string.IsNullOrEmpty(scene.name) ? "未命名场景" : scene.name;
        }

        private static Scene? TryGetPrefabStageScene()
        {
            try
            {
                Assembly editorAssembly = typeof(Editor).Assembly;
                Type utility = editorAssembly.GetType("UnityEditor.SceneManagement.PrefabStageUtility") ??
                    editorAssembly.GetType("UnityEditor.Experimental.SceneManagement.PrefabStageUtility");
                MethodInfo getCurrent = utility == null
                    ? null
                    : utility.GetMethod(
                        "GetCurrentPrefabStage",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object stage = getCurrent == null ? null : getCurrent.Invoke(null, null);
                PropertyInfo sceneProperty = stage == null
                    ? null
                    : stage.GetType().GetProperty(
                        "scene",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                object value = sceneProperty == null ? null : sceneProperty.GetValue(stage, null);
                return value is Scene ? (Scene?)value : null;
            }
            catch (Exception)
            {
                return null;
            }
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
                return dirtyScenes.Count == 0 && dirtyAssets.Count == 0 && busyReasons.Count == 0;
            }
        }
    }
}
