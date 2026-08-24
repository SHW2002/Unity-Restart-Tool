using UnityRestartTool.UI;

namespace UnityRestartTool.Tests;

public sealed class MainFormTests
{
    [Fact]
    public void TryGetBooleanCellValue_IgnoresStatusMessages()
    {
        const string status = "编辑器存在忙碌或未保存状态: 未保存资源";

        bool handled = MainForm.TryGetBooleanCellValue("StatusColumn", status, out bool value);

        Assert.False(handled);
        Assert.False(value);
    }

    [Theory]
    [InlineData("SelectedColumn")]
    [InlineData("ScheduleColumn")]
    public void TryGetBooleanCellValue_ReadsCheckboxColumns(string columnName)
    {
        bool handled = MainForm.TryGetBooleanCellValue(columnName, true, out bool value);

        Assert.True(handled);
        Assert.True(value);
    }
}
