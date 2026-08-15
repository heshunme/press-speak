using HsAsrDictation.Logging;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class LocalLogServiceTests : IDisposable
{
    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), $"press-speak-log-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_logDirectory))
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
    }

    [Fact]
    public void Write_PersistsMessageToLogFile()
    {
        using (var logger = new LocalLogService(_logDirectory))
        {
            logger.Info("单元测试消息");
        }

        var content = File.ReadAllText(Directory.GetFiles(_logDirectory).Single());
        Assert.Contains("INFO", content);
        Assert.Contains("单元测试消息", content);
    }

    [Fact]
    public void Write_AfterDispose_DoesNotThrow()
    {
        var logger = new LocalLogService(_logDirectory);
        logger.Dispose();

        var exception = Record.Exception(() => logger.Info("实例已释放后的写入"));
        Assert.Null(exception);
    }

    [Fact]
    public void Write_RotatesLogFile_WhenSizeLimitExceeded()
    {
        const long maxBytes = 256;
        using (var logger = new LocalLogService(_logDirectory, maxLogFileBytes: maxBytes))
        {
            for (var i = 0; i < 50; i++)
            {
                logger.Info($"第 {i} 条日志，凑满大小上限触发轮转。");
            }
        }

        var files = Directory.GetFiles(_logDirectory);
        var backupFile = files.Single(file => file.EndsWith(".log.old", StringComparison.Ordinal));
        Assert.Contains(files, file => file.EndsWith(".log", StringComparison.Ordinal));
        Assert.True(new FileInfo(backupFile).Length >= maxBytes, "轮转出的 .old 文件应至少达到大小上限。");
    }
}
