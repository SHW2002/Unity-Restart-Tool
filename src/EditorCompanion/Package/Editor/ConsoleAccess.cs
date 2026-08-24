using System;
using System.Reflection;
using UnityEditor;

namespace UnityRestartCompanion
{
    internal static class ConsoleAccess
    {
        private const BindingFlags StaticFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static bool s_Initialized;
        private static MethodInfo s_StartGettingEntries;
        private static MethodInfo s_EndGettingEntries;
        private static MethodInfo s_Clear;

        public static bool TryGetCount(out int count, out string error)
        {
            count = 0;
            if (!EnsureInitialized(out error))
            {
                return false;
            }

            try
            {
                count = (int)s_StartGettingEntries.Invoke(null, null);
                s_EndGettingEntries.Invoke(null, null);
                return true;
            }
            catch (Exception exception)
            {
                error = Unwrap(exception).Message;
                return false;
            }
        }

        public static bool TryClear(out int clearedCount, out string error)
        {
            clearedCount = 0;
            if (!TryGetCount(out clearedCount, out error))
            {
                return false;
            }

            try
            {
                s_Clear.Invoke(null, null);
                return true;
            }
            catch (Exception exception)
            {
                error = Unwrap(exception).Message;
                return false;
            }
        }

        private static bool EnsureInitialized(out string error)
        {
            if (s_Initialized)
            {
                error = s_StartGettingEntries == null ? "UnityEditor.LogEntries API 不可用" : null;
                return s_StartGettingEntries != null;
            }

            s_Initialized = true;
            Type logEntries = typeof(Editor).Assembly.GetType("UnityEditor.LogEntries");
            if (logEntries != null)
            {
                s_StartGettingEntries = logEntries.GetMethod("StartGettingEntries", StaticFlags);
                s_EndGettingEntries = logEntries.GetMethod("EndGettingEntries", StaticFlags);
                s_Clear = logEntries.GetMethod("Clear", StaticFlags, null, Type.EmptyTypes, null);
            }

            if (s_StartGettingEntries == null || s_EndGettingEntries == null || s_Clear == null)
            {
                s_StartGettingEntries = null;
                error = "UnityEditor.LogEntries 反射失败，当前编辑器版本不兼容";
                return false;
            }

            error = null;
            return true;
        }

        private static Exception Unwrap(Exception exception)
        {
            TargetInvocationException invocation = exception as TargetInvocationException;
            return invocation != null && invocation.InnerException != null
                ? invocation.InnerException
                : exception;
        }
    }
}
