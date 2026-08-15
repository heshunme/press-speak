using HsAsrDictation.Asr;
using HsAsrDictation.Models;
using HsAsrDictation.Settings;
using Xunit;

namespace HsAsrDictation.Tests;

/// <summary>
/// 就绪后跳过重复 provisioning 的决策单元：引擎初始化指纹。
/// 指纹（模型根路径；离线含规范化热词）未变时，重复初始化直接短路。
/// </summary>
public sealed class EngineInitFingerprintTests
{
    [Fact]
    public void Matches_SameOfflineSettings_ReturnsTrue()
    {
        var settings = new AppSettings
        {
            OfflineModelRootPath = "/tmp/models/offline",
            Hotwords = "词甲,词乙"
        };

        var captured = EngineInitFingerprint.Capture(settings, AsrModelKind.Offline);

        Assert.True(captured.Matches(EngineInitFingerprint.Capture(settings, AsrModelKind.Offline)));
    }

    [Fact]
    public void Matches_OfflineHotwordsChanged_ReturnsFalse()
    {
        var before = EngineInitFingerprint.Capture(new AppSettings
        {
            OfflineModelRootPath = "/tmp/models/offline",
            Hotwords = "词甲"
        }, AsrModelKind.Offline);

        var after = EngineInitFingerprint.Capture(new AppSettings
        {
            OfflineModelRootPath = "/tmp/models/offline",
            Hotwords = "词甲,词乙"
        }, AsrModelKind.Offline);

        Assert.False(before.Matches(after));
    }

    [Fact]
    public void Matches_OfflineHotwordsEquivalentAfterNormalization_ReturnsTrue()
    {
        var before = EngineInitFingerprint.Capture(new AppSettings
        {
            OfflineModelRootPath = "/tmp/models/offline",
            Hotwords = "词甲,词乙"
        }, AsrModelKind.Offline);

        // 分隔符/空白形式不同但规范化后相同的热词不应触发重建。
        var after = EngineInitFingerprint.Capture(new AppSettings
        {
            OfflineModelRootPath = "/tmp/models/offline",
            Hotwords = "词甲\n词乙\n"
        }, AsrModelKind.Offline);

        Assert.True(before.Matches(after));
    }

    [Fact]
    public void Matches_ModelRootPathChanged_ReturnsFalse()
    {
        var before = EngineInitFingerprint.Capture(new AppSettings
        {
            StreamingModelRootPath = "/tmp/models/streaming-a"
        }, AsrModelKind.Streaming);

        var after = EngineInitFingerprint.Capture(new AppSettings
        {
            StreamingModelRootPath = "/tmp/models/streaming-b"
        }, AsrModelKind.Streaming);

        Assert.False(before.Matches(after));
    }

    [Fact]
    public void Matches_ModelRootPathComparison_IsCaseInsensitive()
    {
        var before = EngineInitFingerprint.Capture(new AppSettings
        {
            StreamingModelRootPath = "C:\\Models\\Streaming"
        }, AsrModelKind.Streaming);

        var after = EngineInitFingerprint.Capture(new AppSettings
        {
            StreamingModelRootPath = "c:\\models\\streaming"
        }, AsrModelKind.Streaming);

        Assert.True(before.Matches(after));
    }

    [Fact]
    public void Capture_Streaming_IgnoresHotwords()
    {
        // 热词只影响离线识别器；流式指纹不含热词，改热词不应导致流式引擎重新 provisioning。
        var before = EngineInitFingerprint.Capture(new AppSettings
        {
            StreamingModelRootPath = "/tmp/models/streaming",
            Hotwords = "词甲"
        }, AsrModelKind.Streaming);

        var after = EngineInitFingerprint.Capture(new AppSettings
        {
            StreamingModelRootPath = "/tmp/models/streaming",
            Hotwords = "完全不同的词"
        }, AsrModelKind.Streaming);

        Assert.True(before.Matches(after));
    }
}
