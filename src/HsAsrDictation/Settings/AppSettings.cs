using HsAsrDictation.Hotkeys;
using HsAsrDictation.Services;

namespace HsAsrDictation.Settings;

public sealed class AppSettings
{
    public HotkeyGesture Hotkey { get; init; } = new();

    public string? PreferredInputDeviceName { get; init; }

    public string ModelRootPath { get; init; } = AppPaths.DefaultModelRootPath;

    public bool AllowClipboardFallback { get; init; } = true;

    public bool AutoDownloadModel { get; init; } = true;

    public static AppSettings CreateDefault() => new();
}
