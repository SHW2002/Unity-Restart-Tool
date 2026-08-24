using System.Globalization;
using System.Text;

namespace UnityRestartTool.Infrastructure;

internal sealed class AppLogger
{
    private const int RetentionDays = 30;
    private const long MaxTotalBytes = 50L * 1024 * 1024;
    private readonly object _syncRoot = new();
    private readonly string _logDirectory;

    public event EventHandler<LogEntry>? EntryWritten;

    public AppLogger(string? logDirectory = null)
    {
        _logDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UnityRestartTool",
            "Logs");
        Directory.CreateDirectory(_logDirectory);
        CleanupOldLogs();
    }

    public string LogDirectory => _logDirectory;

    public void Info(string source, string message) => Write(AppLogLevel.Info, source, message);

    public void Warning(string source, string message) => Write(AppLogLevel.Warning, source, message);

    public void Error(string source, string message, Exception? exception = null)
    {
        string detail = exception is null ? message : $"{message}: {exception.Message}";
        Write(AppLogLevel.Error, source, detail);
    }

    private void Write(AppLogLevel level, string source, string message)
    {
        LogEntry entry = new(
            DateTime.Now,
            level,
            source,
            SensitiveDataRedactor.Redact(message));
        string path = Path.Combine(_logDirectory, $"unity-restart-{entry.Timestamp:yyyy-MM-dd}.log");
        string line = string.Format(
            CultureInfo.InvariantCulture,
            "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] [{2}] {3}{4}",
            entry.Timestamp,
            entry.Level.ToString().ToUpperInvariant(),
            entry.Source,
            entry.Message,
            Environment.NewLine);

        lock (_syncRoot)
        {
            File.AppendAllText(path, line, new UTF8Encoding(false));
        }

        EntryWritten?.Invoke(this, entry);
    }

    private void CleanupOldLogs()
    {
        try
        {
            DirectoryInfo directory = new(_logDirectory);
            FileInfo[] files = directory.GetFiles("unity-restart-*.log")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();
            DateTime oldestAllowed = DateTime.UtcNow.AddDays(-RetentionDays);
            long retainedBytes = 0;

            foreach (FileInfo file in files)
            {
                if (file.LastWriteTimeUtc < oldestAllowed || retainedBytes + file.Length > MaxTotalBytes)
                {
                    file.Delete();
                    continue;
                }

                retainedBytes += file.Length;
            }
        }
        catch
        {
            // Logging must never prevent the application from starting.
        }
    }
}
