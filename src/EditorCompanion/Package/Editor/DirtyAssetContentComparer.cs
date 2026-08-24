using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace UnityRestartCompanion
{
    internal static class DirtyAssetContentComparer
    {
        public static bool TryClearFalsePositive(
            string assetPath,
            IReadOnlyCollection<UnityEngine.Object> dirtyObjects)
        {
            if (dirtyObjects == null || dirtyObjects.Count == 0)
            {
                return true;
            }

            if (IsSourceShader(assetPath, dirtyObjects))
            {
                ClearDirtyFlags(dirtyObjects);
                return true;
            }

            string physicalPath;
            if (!TryGetPhysicalPath(assetPath, out physicalPath) || !File.Exists(physicalPath))
            {
                return false;
            }

            UnityEngine.Object[] diskObjects = null;
            string currentSnapshotPath = null;
            string diskSnapshotPath = null;
            try
            {
                diskObjects = InternalEditorUtility.LoadSerializedFileAndForget(physicalPath);
                if (diskObjects == null || diskObjects.Length == 0)
                {
                    return false;
                }

                UnityEngine.Object[] currentObjects = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                if (currentObjects == null || currentObjects.Length == 0)
                {
                    return false;
                }

                if (!CanCopyCurrentState(currentObjects, diskObjects))
                {
                    return false;
                }

                string comparisonDirectory = Path.Combine(
                    Directory.GetParent(Application.dataPath).FullName,
                    "Library",
                    "UnityRestartTool",
                    "AssetComparisons");
                Directory.CreateDirectory(comparisonDirectory);
                string comparisonId = Guid.NewGuid().ToString("N");
                currentSnapshotPath = Path.Combine(comparisonDirectory, comparisonId + ".current.asset");
                diskSnapshotPath = Path.Combine(comparisonDirectory, comparisonId + ".disk.asset");

                InternalEditorUtility.SaveToSerializedFileAndForget(
                    diskObjects,
                    diskSnapshotPath,
                    true);
                CopyCurrentState(currentObjects, diskObjects);
                InternalEditorUtility.SaveToSerializedFileAndForget(
                    diskObjects,
                    currentSnapshotPath,
                    true);

                bool unchanged = FilesEqual(currentSnapshotPath, diskSnapshotPath);
                if (unchanged)
                {
                    ClearDirtyFlags(dirtyObjects);
                }
                return unchanged;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                DeleteBestEffort(currentSnapshotPath);
                DeleteBestEffort(diskSnapshotPath);
                DestroyLoadedObjects(diskObjects);
            }
        }

        private static bool IsSourceShader(
            string assetPath,
            IEnumerable<UnityEngine.Object> dirtyObjects)
        {
            return assetPath.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) &&
                dirtyObjects.All(value => value is Shader);
        }

        private static bool TryGetPhysicalPath(string assetPath, out string physicalPath)
        {
            physicalPath = string.Empty;
            if (string.IsNullOrEmpty(assetPath) ||
                (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                 !assetPath.StartsWith("ProjectSettings/", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            string projectPath = Directory.GetParent(Application.dataPath).FullName;
            physicalPath = Path.GetFullPath(Path.Combine(
                projectPath,
                assetPath.Replace('/', Path.DirectorySeparatorChar)));
            return physicalPath.StartsWith(projectPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool CanCopyCurrentState(
            UnityEngine.Object[] currentObjects,
            UnityEngine.Object[] diskObjects)
        {
            return currentObjects.Length == 1 &&
                diskObjects.Length == 1 &&
                currentObjects[0] != null &&
                diskObjects[0] != null &&
                currentObjects[0].GetType() == diskObjects[0].GetType();
        }

        private static void CopyCurrentState(
            UnityEngine.Object[] currentObjects,
            UnityEngine.Object[] diskObjects)
        {
            EditorUtility.CopySerialized(currentObjects[0], diskObjects[0]);
        }

        private static bool FilesEqual(string firstPath, string secondPath)
        {
            FileInfo first = new FileInfo(firstPath);
            FileInfo second = new FileInfo(secondPath);
            if (first.Length != second.Length)
            {
                return false;
            }

            const int BufferSize = 81920;
            byte[] firstBuffer = new byte[BufferSize];
            byte[] secondBuffer = new byte[BufferSize];
            using (FileStream firstStream = File.OpenRead(firstPath))
            using (FileStream secondStream = File.OpenRead(secondPath))
            {
                while (true)
                {
                    int firstRead = firstStream.Read(firstBuffer, 0, firstBuffer.Length);
                    int secondRead = secondStream.Read(secondBuffer, 0, secondBuffer.Length);
                    if (firstRead != secondRead)
                    {
                        return false;
                    }
                    if (firstRead == 0)
                    {
                        return true;
                    }

                    for (int index = 0; index < firstRead; index++)
                    {
                        if (firstBuffer[index] != secondBuffer[index])
                        {
                            return false;
                        }
                    }
                }
            }
        }

        private static void ClearDirtyFlags(IEnumerable<UnityEngine.Object> objects)
        {
            foreach (UnityEngine.Object value in objects)
            {
                if (value != null)
                {
                    EditorUtility.ClearDirty(value);
                }
            }
        }

        private static void DestroyLoadedObjects(IEnumerable<UnityEngine.Object> objects)
        {
            if (objects == null)
            {
                return;
            }

            foreach (UnityEngine.Object value in objects)
            {
                if (value != null)
                {
                    UnityEngine.Object.DestroyImmediate(value);
                }
            }
        }

        private static void DeleteBestEffort(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
