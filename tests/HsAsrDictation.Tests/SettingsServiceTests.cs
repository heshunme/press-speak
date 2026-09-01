using HsAsrDictation.Logging;
using HsAsrDictation.Settings;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDirectory;

    public SettingsServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"hs-asr-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch
        {
        }
    }

    private SettingsService CreateService(out LocalLogService logger)
    {
        logger = new LocalLogService(Path.Combine(_tempDirectory, "logs"));
        return new SettingsService(Path.Combine(_tempDirectory, "settings.json"), logger);
    }

    [Fact]
    public void Save_RaisesSettingsChangedWithPreviousAndCurrent()
    {
        var service = CreateService(out var logger);
        using var _ = logger;
        service.Load();
        var original = service.Current;

        SettingsChangedEventArgs? observed = null;
        service.SettingsChanged += (_, args) => observed = args;

        var updated = new AppSettings
        {
            EnablePunctuation = !original.EnablePunctuation
        };
        service.Save(updated);

        Assert.NotNull(observed);
        Assert.Same(original, observed!.Previous);
        Assert.Same(service.Current, observed.Current);
        Assert.Equal(!original.EnablePunctuation, observed.Current.EnablePunctuation);
    }

    [Fact]
    public void Load_LegacySettingsWithoutVadToggle_DefaultsToEnabled()
    {
        var settingsPath = Path.Combine(_tempDirectory, "settings.json");
        // 模拟接入分段解码之前写下的配置文件：完全没有该键。
        File.WriteAllText(settingsPath, "{ \"EnablePunctuation\": true }");

        using var logger = new LocalLogService(Path.Combine(_tempDirectory, "logs"));
        var service = new SettingsService(settingsPath, logger);
        service.Load();

        Assert.True(service.Current.EnableVadSegmentedDecoding);
        Assert.True(service.Current.EnablePunctuation);
    }

    [Fact]
    public void Save_WhenWriteFails_DoesNotRaiseSettingsChangedOrMutateCurrent()
    {
        var service = CreateService(out var logger);
        using var _ = logger;
        service.Load();
        var before = service.Current;

        // 把 settings.json 变成目录，使原子写入失败。
        var settingsPath = Path.Combine(_tempDirectory, "settings.json");
        File.Delete(settingsPath);
        Directory.CreateDirectory(settingsPath);

        var raised = false;
        service.SettingsChanged += (_, _) => raised = true;

        Assert.ThrowsAny<Exception>(() => service.Save(new AppSettings()));
        Assert.False(raised);
        Assert.Same(before, service.Current);
    }

    [Fact]
    public void Load_DoesNotRaiseSettingsChanged()
    {
        var service = CreateService(out var logger);
        using var _ = logger;

        var raised = false;
        service.SettingsChanged += (_, _) => raised = true;
        service.Load();

        Assert.False(raised);
    }
}
