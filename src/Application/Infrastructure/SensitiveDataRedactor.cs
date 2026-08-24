using System.Text.RegularExpressions;

namespace UnityRestartTool.Infrastructure;

internal static partial class SensitiveDataRedactor
{
    [GeneratedRegex(
        """(?i)(-accessToken|-hubSessionId|-licensingIpc)(?:\s+|=)(?:"[^"]*"|\S+)""",
        RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveArgumentRegex();

    public static string Redact(string value) =>
        SensitiveArgumentRegex().Replace(value ?? string.Empty, "$1 <redacted>");
}
