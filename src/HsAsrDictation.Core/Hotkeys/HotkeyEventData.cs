namespace HsAsrDictation.Hotkeys;

public readonly record struct HotkeyEventData(
    int VirtualKey,
    int ScanCode,
    bool IsExtendedKey,
    bool IsKeyDown,
    bool IsAltContext,
    bool IsInjected)
{
    public bool TryGetPhysicalKey(out HotkeyPhysicalKey key) =>
        HotkeyPhysicalKey.TryCreate(this, out key);
}
