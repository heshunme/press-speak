using System.Diagnostics;
using System.IO;

namespace HsAsrDictation.Logging;

public sealed class LocalLogService : IDisposable
{
    private const long DefaultMaxLogFileBytes = 10 * 1024 * 1024;

    private readonly object _syncRoot = new();
    private readonly string _logFilePath;
    private readonly long _maxLogFileBytes;
    private StreamWriter? _writer;
    private long _bytesWritten;

    public LocalLogService(string logDirectory, long maxLogFileBytes = DefaultMaxLogFileBytes)
    {
        _logFilePath = Path.Combine(logDirectory, $"dictation-{DateTime.UtcNow:yyyyMMdd}.log");
        _maxLogFileBytes = maxLogFileBytes;
        try
        {
            Directory.CreateDirectory(logDirectory);
            _bytesWritten = File.Exists(_logFilePath) ? new FileInfo(_logFilePath).Length : 0;
            _writer = CreateWriter();
        }
        catch (Exception ex)
        {
            // 日志器任何情况下都不抛出：初始化失败时降级为静默，由 Debug 输出兜底。
            Debug.WriteLine($"初始化日志文件失败：{ex}");
        }
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
        lock (_syncRoot)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Write(string level, string message)
    {
        lock (_syncRoot)
        {
            // 磁盘满、文件被占用、实例已 Dispose 等情况一律吞掉：日志永远不能拖垮调用方
            // （低级键盘钩子回调、NAudio 录音线程都在调用链上，catch 块里记日志也不能掩盖原始异常）。
            try
            {
                if (_writer is null)
                {
                    return;
                }

                var line = $"{DateTimeOffset.Now:O} [{level}] {message}";
                _writer.WriteLine(line);
                _bytesWritten += line.Length + Environment.NewLine.Length;
                if (_bytesWritten >= _maxLogFileBytes)
                {
                    RotateLogFile();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"写入日志失败：{ex}");
            }
        }
    }

    /// <summary>超过大小上限时把当前日志改名为 .old 并重新开始；调用方须已持有 _syncRoot，失败向 Write 的 catch 冒泡。</summary>
    private void RotateLogFile()
    {
        var oldWriter = _writer;
        _writer = null;
        oldWriter?.Dispose();

        File.Move(_logFilePath, _logFilePath + ".old", overwrite: true);
        _writer = CreateWriter();
        _bytesWritten = 0;
    }

    private StreamWriter CreateWriter() =>
        new(new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
}
