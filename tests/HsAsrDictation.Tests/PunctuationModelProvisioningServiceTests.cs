using System.Text;
using HsAsrDictation.Logging;
using HsAsrDictation.Models;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class PunctuationModelProvisioningServiceTests
{
    [Fact]
    public async Task EnsureReadyAsync_ReturnsNotReady_WhenModelIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var logDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            using var logger = new LocalLogService(logDir);
            var service = new PunctuationModelProvisioningService(logger, root);

            var result = await service.EnsureReadyAsync(downloadIfMissing: false);

            Assert.False(result.IsReady);
            Assert.Contains(PunctuationModelManifest.RequiredFileName, result.MissingEntries);
            Assert.Contains(root, result.ErrorMessage);
        }
        finally
        {
            TryDeleteDirectory(root);
            TryDeleteDirectory(logDir);
        }
    }

    [Fact]
    public async Task EnsureReadyAsync_ReturnsReady_WhenModelFileExists()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var logDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var modelDirectory = Path.Combine(root, PunctuationModelManifest.ExtractedDirectoryName);
        var modelPath = Path.Combine(modelDirectory, PunctuationModelManifest.RequiredFileName);

        try
        {
            Directory.CreateDirectory(modelDirectory);
            await File.WriteAllTextAsync(modelPath, "placeholder");

            using var logger = new LocalLogService(logDir);
            var service = new PunctuationModelProvisioningService(logger, root);

            var result = await service.EnsureReadyAsync(downloadIfMissing: false);

            Assert.True(result.IsReady);
            Assert.Equal(modelDirectory, result.ModelDirectory);
        }
        finally
        {
            TryDeleteDirectory(root);
            TryDeleteDirectory(logDir);
        }
    }

    [Fact]
    public async Task DownloadAsync_ExtractsArchiveToModelDirectory_WhenDownloadSucceeds()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var logDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            var payload = Encoding.UTF8.GetBytes("fake onnx payload");
            var archive = ProvisioningTestDoubles.BuildTarBz2(
                $"pkg/{PunctuationModelManifest.RequiredFileName}",
                payload);
            using var logger = new LocalLogService(logDir);
            var service = new PunctuationModelProvisioningService(
                logger,
                root,
                ProvisioningTestDoubles.RespondingWithBytes(archive),
                TimeSpan.FromMinutes(1));

            var result = await service.DownloadAsync();

            Assert.True(result.IsReady);
            var expectedFile = Path.Combine(
                root,
                PunctuationModelManifest.ExtractedDirectoryName,
                PunctuationModelManifest.RequiredFileName);
            Assert.True(File.Exists(expectedFile));
            Assert.Equal(payload, await File.ReadAllBytesAsync(expectedFile));
            // 压缩包与临时解压目录已清理。
            Assert.False(File.Exists(Path.Combine(
                root,
                $"{PunctuationModelManifest.ExtractedDirectoryName}.tar.bz2")));
        }
        finally
        {
            TryDeleteDirectory(root);
            TryDeleteDirectory(logDir);
        }
    }

    [Fact]
    public async Task DownloadAsync_ThrowsTimeoutException_WhenDownloadBodyStalls()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var logDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            using var logger = new LocalLogService(logDir);
            var service = new PunctuationModelProvisioningService(
                logger,
                root,
                ProvisioningTestDoubles.HangingBody(),
                TimeSpan.FromMilliseconds(500));

            await Assert.ThrowsAsync<TimeoutException>(() => service.DownloadAsync());
            // 半成品压缩包已清理。
            Assert.False(File.Exists(Path.Combine(
                root,
                $"{PunctuationModelManifest.ExtractedDirectoryName}.tar.bz2")));
        }
        finally
        {
            TryDeleteDirectory(root);
            TryDeleteDirectory(logDir);
        }
    }

    [Fact]
    public async Task DownloadAsync_PropagatesCallerCancellation_WhenDownloadBodyStalls()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var logDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            using var logger = new LocalLogService(logDir);
            var service = new PunctuationModelProvisioningService(
                logger,
                root,
                ProvisioningTestDoubles.HangingBody(),
                TimeSpan.FromMinutes(10));
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

            // 调用方主动取消应原样传播 OperationCanceledException，而非被包装成 TimeoutException。
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(cts.Token));
        }
        finally
        {
            TryDeleteDirectory(root);
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
