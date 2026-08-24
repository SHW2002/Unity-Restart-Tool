namespace UnityRestartTool.Models;

internal sealed record EditorInstance(
    int ProcessId,
    EditorKind Kind,
    string ExecutablePath,
    string ProjectPath,
    string ProjectName,
    string EditorVersion,
    DateTime StartTime,
    IntPtr MainWindowHandle,
    string WindowTitle,
    int WindowOrder)
{
    public TimeSpan RunningTime => DateTime.Now - StartTime;
}
