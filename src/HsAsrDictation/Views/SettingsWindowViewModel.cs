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

        SelectedDeviceName = settings.PreferredInputDeviceName;
        ModelRootPath = settings.ModelRootPath;
        AllowClipboardFallback = settings.AllowClipboardFallback;
        AutoDownloadModel = settings.AutoDownloadModel;
        SelectedKey = settings.Hotkey.Key;
        UseCtrl = settings.Hotkey.Modifiers.HasFlag(HotkeyModifiers.Control);
        UseAlt = settings.Hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt);
        UseShift = settings.Hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift);
        UseWindows = settings.Hotkey.Modifiers.HasFlag(HotkeyModifiers.Windows);
    }

    public ObservableCollection<AudioDeviceInfo> Devices { get; }

    public ObservableCollection<Key> AvailableKeys { get; }

    public string? SelectedDeviceName { get; set; }

    public string ModelRootPath { get; set; }

    public bool AllowClipboardFallback { get; set; }

    public bool AutoDownloadModel { get; set; }

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
            ModelRootPath = ModelRootPath,
            AllowClipboardFallback = AllowClipboardFallback,
            AutoDownloadModel = AutoDownloadModel,
            Hotkey = new HotkeyGesture
            {
                Modifiers = modifiers,
                Key = SelectedKey
            }
        };
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
}
