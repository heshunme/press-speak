using HsAsrDictation.Asr;
using HsAsrDictation.Logging;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class SherpaOfflinePunctuationServiceTests
{
    [Fact]
    public void TryAddPunctuation_ReturnsOriginalText_WhenDisabled()
    {
        var logDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            using var logger = new LocalLogService(logDir);
            using var service = new SherpaOfflinePunctuationService(logger);

            service.Reload(new PunctuationRuntimeOptions
            {
                Enabled = false,
                ModelPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "model.int8.onnx"),
                NumThreads = 1
            });

            var text = "我们都是木头人不会说话不会动";

            Assert.Equal(text, service.TryAddPunctuation(text));
            Assert.False(service.IsReady);
            Assert.False(service.IsEnabled);
        }
        finally
        {
            TryDeleteDirectory(logDir);
        }
    }

    [Fact]
    public void Reload_DoesNotThrow_WhenModelFileIsMissing()
    {
        var logDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            using var logger = new LocalLogService(logDir);
            using var service = new SherpaOfflinePunctuationService(logger);

            service.Reload(new PunctuationRuntimeOptions
            {
                Enabled = true,
                ModelPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "model.int8.onnx"),
                NumThreads = 1
            });

            Assert.False(service.IsReady);
            Assert.True(service.IsEnabled);
        }
        finally
        {
            TryDeleteDirectory(logDir);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
