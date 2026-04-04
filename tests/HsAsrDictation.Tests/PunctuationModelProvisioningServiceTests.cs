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
