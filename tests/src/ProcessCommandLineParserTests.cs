using UnityRestartTool.Services;

namespace UnityRestartTool.Tests;

public sealed class ProcessCommandLineParserTests
{
    [Theory]
    [InlineData(
        "\"C:\\Program Files\\Unity\\Editor\\Unity.exe\" -projectPath \"E:\\Unity Projects\\Garden\" -useHub",
        "E:\\Unity Projects\\Garden")]
    [InlineData(
        "D:\\Develop\\Tuanjie\\Editor\\Tuanjie.exe -projectpath E:\\Unity\\Garden -useHub",
        "E:\\Unity\\Garden")]
    [InlineData(
        "Unity.exe --projectPath=E:/Unity/Garden",
        "E:\\Unity\\Garden")]
    public void TryGetProjectPath_ParsesSupportedForms(string commandLine, string expected)
    {
        bool found = ProcessCommandLineParser.TryGetProjectPath(commandLine, out string actual);

        Assert.True(found);
        Assert.Equal(Path.GetFullPath(expected), actual, ignoreCase: true);
    }

    [Theory]
    [InlineData("Tuanjie.exe -adb2 -batchMode -name AssetImportWorker0", true)]
    [InlineData("Unity.exe -projectPath E:\\Unity\\Garden", false)]
    [InlineData("Unity.exe -projectPath E:\\Unity\\Garden -batchMode", true)]
    public void IsWorkerOrBatchProcess_FiltersBackgroundProcesses(string commandLine, bool expected)
    {
        Assert.Equal(expected, ProcessCommandLineParser.IsWorkerOrBatchProcess(commandLine));
    }
}
