using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UnityRestartTool.Interop;

namespace UnityRestartTool.Services;

internal static partial class ProcessCommandLineParser
{
    [GeneratedRegex(
        """(?i)(?:^|\s)-{1,2}projectpath(?:\s+|=)(?:"(?<quoted>[^"]+)"|(?<plain>\S+))""",
        RegexOptions.CultureInvariant)]
    private static partial Regex ProjectPathRegex();

    public static IReadOnlyList<string> Split(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        IntPtr argumentsPointer = NativeMethods.CommandLineToArgvW(commandLine, out int count);
        if (argumentsPointer == IntPtr.Zero)
        {
            return [];
        }

        try
        {
            string[] arguments = new string[count];
            for (int index = 0; index < count; index++)
            {
                IntPtr argumentPointer = Marshal.ReadIntPtr(argumentsPointer, index * IntPtr.Size);
                arguments[index] = Marshal.PtrToStringUni(argumentPointer) ?? string.Empty;
            }

            return arguments;
        }
        finally
        {
            NativeMethods.LocalFree(argumentsPointer);
        }
    }

    public static bool TryGetProjectPath(string commandLine, out string projectPath)
    {
        IReadOnlyList<string> arguments = Split(commandLine);
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (string.Equals(argument, "-projectPath", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(argument, "--projectPath", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 < arguments.Count && !string.IsNullOrWhiteSpace(arguments[index + 1]))
                {
                    return TryNormalize(arguments[index + 1], out projectPath);
                }
            }

            int equalsIndex = argument.IndexOf('=');
            if (equalsIndex > 0 &&
                (string.Equals(argument[..equalsIndex], "-projectPath", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(argument[..equalsIndex], "--projectPath", StringComparison.OrdinalIgnoreCase)))
            {
                return TryNormalize(argument[(equalsIndex + 1)..], out projectPath);
            }
        }

        Match fallback = ProjectPathRegex().Match(commandLine);
        if (fallback.Success)
        {
            string value = fallback.Groups["quoted"].Success
                ? fallback.Groups["quoted"].Value
                : fallback.Groups["plain"].Value;
            return TryNormalize(value, out projectPath);
        }

        projectPath = string.Empty;
        return false;
    }

    public static bool IsWorkerOrBatchProcess(string commandLine)
    {
        IReadOnlyList<string> arguments = Split(commandLine);
        return arguments.Any(argument =>
            string.Equals(argument, "-batchMode", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, "-adb2", StringComparison.OrdinalIgnoreCase)) ||
            commandLine.Contains("AssetImportWorker", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalize(string value, out string projectPath)
    {
        try
        {
            projectPath = Path.GetFullPath(value.Trim().Trim('"'))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return !string.IsNullOrWhiteSpace(projectPath);
        }
        catch
        {
            projectPath = string.Empty;
            return false;
        }
    }
}
