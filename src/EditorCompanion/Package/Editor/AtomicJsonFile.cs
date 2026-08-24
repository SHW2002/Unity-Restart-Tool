using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace UnityRestartCompanion
{
    internal static class AtomicJsonFile
    {
        public static void Write(string path, object value)
        {
            string temporaryPath = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(value), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temporaryPath, path, null);
                    return;
                }
                catch (IOException)
                {
                    File.Delete(path);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Delete(path);
                }
            }
            File.Move(temporaryPath, path);
        }

        public static void DeleteBestEffort(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
