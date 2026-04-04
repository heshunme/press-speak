using System.Collections.ObjectModel;
using System.Windows.Input;
using HsAsrDictation.Audio;
using HsAsrDictation.Hotkeys;
using HsAsrDictation.Settings;

namespace HsAsrDictation.Views;

public sealed class SettingsWindowViewModel
{
    public SettingsWindowViewModel(AppSettings settings, IReadOnlyList<AudioDeviceInfo> devices)
    {
        Devices = new ObservableCollection<AudioDeviceInfo>(devices);
        AvailableKeys = new ObservableCollection<Key>(BuildAvailableKeys());
        RecognitionModes = new ObservableCollection<RecognitionModeOption>(BuildRecognitionModes());

        SelectedDeviceName = settings.PreferredInputDeviceName;
        OfflineModelRootPath = settings.OfflineModelRootPath;
        StreamingModelRootPath = settings.StreamingModelRootPath;
        AllowClipboardFallback = settings.AllowClipboardFallback;
        AutoDownloadModel = settings.AutoDownloadModel;
        EnablePunctuation = settings.EnablePunctuation;
        EnableStreamingPreview = settings.EnableStreamingPreview;
        SelectedRecognitionMode = RecognitionModes.First(x => x.Mode == settings.RecognitionMode);
        SelectedKey = settings.Hotkey.Key;
        UseCtrl = settings.Hotkey.Modifiers.HasFlag(HotkeyModifiers.Control);
        UseAlt = settings.Hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt);
        UseShift = settings.Hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift);
        UseWindows = settings.Hotkey.Modifiers.HasFlag(HotkeyModifiers.Windows);
    }

    public ObservableCollection<AudioDeviceInfo> Devices { get; }

    public ObservableCollection<Key> AvailableKeys { get; }

    public ObservableCollection<RecognitionModeOption> RecognitionModes { get; }

    public string? SelectedDeviceName { get; set; }

    public string OfflineModelRootPath { get; set; }

    public string StreamingModelRootPath { get; set; }

    public bool AllowClipboardFallback { get; set; }

    public bool AutoDownloadModel { get; set; }

    public bool EnablePunctuation { get; set; }

    public bool EnableStreamingPreview { get; set; }

    public RecognitionModeOption SelectedRecognitionMode { get; set; } = null!;

    public bool UseCtrl { get; set; }

    public bool UseAlt { get; set; }

    public bool UseShift { get; set; }

    public bool UseWindows { get; set; }

    public Key SelectedKey { get; set; }

    public AppSettings ToSettings()
    {
        var modifiers = HotkeyModifiers.None;

        if (UseCtrl)
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (UseAlt)
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (UseShift)
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (UseWindows)
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        return new AppSettings
        {
            PreferredInputDeviceName = string.IsNullOrWhiteSpace(SelectedDeviceName)
                ? null
                : SelectedDeviceName,
            ModelRootPath = ResolveModelRootPath(),
            OfflineModelRootPath = OfflineModelRootPath,
            StreamingModelRootPath = StreamingModelRootPath,
            AllowClipboardFallback = AllowClipboardFallback,
            AutoDownloadModel = AutoDownloadModel,
            EnablePunctuation = EnablePunctuation,
            RecognitionMode = SelectedRecognitionMode.Mode,
            EnableStreamingPreview = EnableStreamingPreview,
            Hotkey = new HotkeyGesture
            {
                Modifiers = modifiers,
                Key = SelectedKey
            }
        };
    }

    private static IEnumerable<RecognitionModeOption> BuildRecognitionModes()
    {
        yield return new RecognitionModeOption(RecognitionMode.Hybrid, "混合模式");
        yield return new RecognitionModeOption(RecognitionMode.StreamingOnly, "纯流式");
        yield return new RecognitionModeOption(RecognitionMode.NonStreaming, "非流式");
    }

    private string ResolveModelRootPath()
    {
        if (!string.IsNullOrWhiteSpace(OfflineModelRootPath) &&
            !string.IsNullOrWhiteSpace(StreamingModelRootPath))
        {
            var offlineParent = System.IO.Path.GetDirectoryName(OfflineModelRootPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            var streamingParent = System.IO.Path.GetDirectoryName(StreamingModelRootPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

            if (!string.IsNullOrWhiteSpace(offlineParent) &&
                string.Equals(offlineParent, streamingParent, StringComparison.OrdinalIgnoreCase))
            {
                return offlineParent;
            }
        }

        return Services.AppPaths.DefaultModelRootPath;
    }

    private static IEnumerable<Key> BuildAvailableKeys()
    {
        yield return Key.Space;
        yield return Key.Oem3;

        foreach (var key in Enum.GetValues<Key>())
        {
            if (key is >= Key.A and <= Key.Z)
            {
                yield return key;
            }
        }

        foreach (var key in Enum.GetValues<Key>())
        {
            if (key is >= Key.D0 and <= Key.D9)
            {
                yield return key;
            }
        }

        foreach (var key in Enum.GetValues<Key>())
        {
            if (key is >= Key.F1 and <= Key.F12)
            {
                yield return key;
            }
        }
    }

    public sealed record RecognitionModeOption(RecognitionMode Mode, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
