using HsAsrDictation.Asr;
using HsAsrDictation.Hotkeys;
using HsAsrDictation.Services;
using System.IO;

namespace HsAsrDictation.Settings;

public sealed class AppSettings
{
    public const int DefaultMaxRecordingDurationSeconds = 300;
    public const int MinMaxRecordingDurationSeconds = 5;
    public const int MaxMaxRecordingDurationSeconds = 1800;
    public const int DefaultHotkeyReleaseTailDurationMilliseconds = 1000;
    public const int MinHotkeyReleaseTailDurationMilliseconds = 0;
    public const int MaxHotkeyReleaseTailDurationMilliseconds = 5000;

    public HotkeyGesture Hotkey { get; init; } = HotkeyGesture.CreateDefault();

    public string? PreferredInputDeviceName { get; init; }

    public string ModelRootPath { get; init; } = AppPaths.DefaultModelRootPath;

    public string OfflineModelRootPath { get; init; } = Path.Combine(AppPaths.DefaultModelRootPath, "offline");

    public string StreamingModelRootPath { get; init; } = Path.Combine(AppPaths.DefaultModelRootPath, "streaming");

    public bool AllowClipboardFallback { get; init; } = true;

    public bool AutoDownloadModel { get; init; } = true;

    public bool EnablePunctuation { get; init; } = false;

    public bool EnablePostProcessingRules { get; init; } = true;

    public RecognitionMode RecognitionMode { get; init; } = RecognitionMode.Hybrid;

    public bool EnableStreamingPreview { get; init; } = true;

    public int MaxRecordingDurationSeconds { get; init; } = DefaultMaxRecordingDurationSeconds;

    public int HotkeyReleaseTailDurationMilliseconds { get; init; } = DefaultHotkeyReleaseTailDurationMilliseconds;

    /// <summary>识别热词（每行一个词的规范存储形式），仅作用于离线 FunASR-Nano 引擎。</summary>
    public string Hotwords { get; init; } = string.Empty;

    public AppSettings Normalize()
    {
        var modelRootPath = string.IsNullOrWhiteSpace(ModelRootPath)
            ? AppPaths.DefaultModelRootPath
            : ModelRootPath;

        var offlineModelRootPath = string.IsNullOrWhiteSpace(OfflineModelRootPath)
            ? Path.Combine(modelRootPath, "offline")
            : OfflineModelRootPath;

        var streamingModelRootPath = string.IsNullOrWhiteSpace(StreamingModelRootPath)
            ? Path.Combine(modelRootPath, "streaming")
            : StreamingModelRootPath;

        var maxRecordingDurationSeconds = Math.Clamp(
            MaxRecordingDurationSeconds,
            MinMaxRecordingDurationSeconds,
            MaxMaxRecordingDurationSeconds);

        var hotkeyReleaseTailDurationMilliseconds = Math.Clamp(
            HotkeyReleaseTailDurationMilliseconds,
            MinHotkeyReleaseTailDurationMilliseconds,
            MaxHotkeyReleaseTailDurationMilliseconds);

        return new AppSettings
        {
            Hotkey = (Hotkey is not null && Hotkey.IsValid ? Hotkey : HotkeyGesture.CreateDefault()).Normalize(),
            PreferredInputDeviceName = PreferredInputDeviceName,
            ModelRootPath = modelRootPath,
            OfflineModelRootPath = offlineModelRootPath,
            StreamingModelRootPath = streamingModelRootPath,
            AllowClipboardFallback = AllowClipboardFallback,
            AutoDownloadModel = AutoDownloadModel,
            EnablePunctuation = EnablePunctuation,
            EnablePostProcessingRules = EnablePostProcessingRules,
            RecognitionMode = RecognitionMode,
            EnableStreamingPreview = EnableStreamingPreview,
            MaxRecordingDurationSeconds = maxRecordingDurationSeconds,
            HotkeyReleaseTailDurationMilliseconds = hotkeyReleaseTailDurationMilliseconds,
            Hotwords = HotwordsNormalizer.NormalizeToStorage(Hotwords)
        };
    }

    public static AppSettings CreateDefault() => new()
    {
        Hotkey = HotkeyGesture.CreateDefault()
    };
}
