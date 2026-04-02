using System.IO;

namespace HsAsrDictation.Logging;

public sealed class LocalLogService : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly string _logFilePath;
    private readonly StreamWriter _writer;

    public LocalLogService(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        _logFilePath = Path.Combine(logDirectory, $"dictation-{DateTime.UtcNow:yyyyMMdd}.log");
        _writer = new StreamWriter(
            new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    public string LogFilePath => _logFilePath;

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? ex = null)
    {
        var suffix = ex is null ? string.Empty : $" | {ex}";
        Write("ERROR", $"{message}{suffix}");
    }

    public void Dispose()
    {
        _writer.Dispose();
    }

    private void Write(string level, string message)
    {
        lock (_syncRoot)
        {
            _writer.WriteLine($"{DateTimeOffset.Now:O} [{level}] {message}");
        }
    }
}
