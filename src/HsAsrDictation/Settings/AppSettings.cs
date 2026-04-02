using HsAsrDictation.Hotkeys;
using HsAsrDictation.Services;
using System.IO;

namespace HsAsrDictation.Settings;

public sealed class AppSettings
{
    public HotkeyGesture Hotkey { get; init; } = new();

    public string? PreferredInputDeviceName { get; init; }

    public string ModelRootPath { get; init; } = AppPaths.DefaultModelRootPath;

    public string OfflineModelRootPath { get; init; } = Path.Combine(AppPaths.DefaultModelRootPath, "offline");

    public string StreamingModelRootPath { get; init; } = Path.Combine(AppPaths.DefaultModelRootPath, "streaming");

    public bool AllowClipboardFallback { get; init; } = true;

    public bool AutoDownloadModel { get; init; } = true;

    public RecognitionMode RecognitionMode { get; init; } = RecognitionMode.Hybrid;

    public bool EnableStreamingPreview { get; init; } = true;

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

        return new AppSettings
        {
            Hotkey = Hotkey ?? new HotkeyGesture(),
            PreferredInputDeviceName = PreferredInputDeviceName,
            ModelRootPath = modelRootPath,
            OfflineModelRootPath = offlineModelRootPath,
            StreamingModelRootPath = streamingModelRootPath,
            AllowClipboardFallback = AllowClipboardFallback,
            AutoDownloadModel = AutoDownloadModel,
            RecognitionMode = RecognitionMode,
            EnableStreamingPreview = EnableStreamingPreview
        };
    }

    public static AppSettings CreateDefault() => new();
}
