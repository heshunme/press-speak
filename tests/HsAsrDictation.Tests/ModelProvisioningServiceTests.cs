using HsAsrDictation.Logging;
using HsAsrDictation.Models;
using HsAsrDictation.Settings;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class ModelProvisioningServiceTests
{
    [Fact]
    public async Task DownloadAsync_ThrowsTimeoutException_WhenDownloadBodyStalls()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var logDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");

        try
        {
            using var logger = new LocalLogService(logDir);
            var settingsService = CreateSettingsService(settingsPath, logger, root);
            var service = new ModelProvisioningService(
                settingsService,
                logger,
                ProvisioningTestDoubles.HangingBody(),
                TimeSpan.FromMilliseconds(500));

            await Assert.ThrowsAsync<TimeoutException>(() => service.DownloadAsync(AsrModelKind.Offline));

            // 半成品压缩包已清理。
            var archivePath = Path.Combine(
                root,
                "offline",
                $"{ModelManifest.GetDefinition(AsrModelKind.Offline).ExtractedDirectoryName}.tar.bz2");
            Assert.False(File.Exists(archivePath));
        }
        finally
        {
            TryDeleteDirectory(root);
            TryDeleteDirectory(logDir);
            TryDeleteDirectory(Path.GetDirectoryName(settingsPath)!);
        }
    }

    [Fact]
    public async Task DownloadAsync_PropagatesCallerCancellation_WhenDownloadBodyStalls()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var logDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");

        try
        {
            using var logger = new LocalLogService(logDir);
            var settingsService = CreateSettingsService(settingsPath, logger, root);
            var service = new ModelProvisioningService(
                settingsService,
                logger,
                ProvisioningTestDoubles.HangingBody(),
                TimeSpan.FromMinutes(10));
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

            // 调用方主动取消应原样传播 OperationCanceledException，而非被包装成 TimeoutException。
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.DownloadAsync(AsrModelKind.Offline, cts.Token));
        }
        finally
        {
            TryDeleteDirectory(root);
            TryDeleteDirectory(logDir);
            TryDeleteDirectory(Path.GetDirectoryName(settingsPath)!);
        }
    }

    private static SettingsService CreateSettingsService(string settingsPath, LocalLogService logger, string modelRootPath)
    {
        var defaults = AppSettings.CreateDefault();
        var settingsService = new SettingsService(settingsPath, logger);
        settingsService.Save(new AppSettings
        {
            Hotkey = defaults.Hotkey,
            OfflineModelRootPath = Path.Combine(modelRootPath, "offline"),
            StreamingModelRootPath = Path.Combine(modelRootPath, "streaming")
        });
        return settingsService;
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
