using UnityRestartTool.Infrastructure;

namespace UnityRestartTool.Tests;

public sealed class SensitiveDataRedactorTests
{
    [Fact]
    public void Redact_RemovesHubAndLicensingValues()
    {
        const string input =
            "Unity.exe -accessToken abc123 -hubSessionId=secret -licensingIpc \"LicenseClient-user\" -projectPath E:\\Unity\\Garden";

        string output = SensitiveDataRedactor.Redact(input);

        Assert.DoesNotContain("abc123", output);
        Assert.DoesNotContain("secret", output);
        Assert.DoesNotContain("LicenseClient-user", output);
        Assert.Contains("E:\\Unity\\Garden", output);
        Assert.Equal(3, output.Split("<redacted>").Length - 1);
    }
}
