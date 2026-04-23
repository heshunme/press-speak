namespace HsAsrDictation.Hotkeys;

public readonly record struct HotkeyBindingSnapshot(
    HotkeyModifiers Modifiers,
    int VirtualKey,
    int PrimaryScanCode,
    bool IsExtendedKey);
