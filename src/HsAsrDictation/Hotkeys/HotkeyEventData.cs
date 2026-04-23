namespace HsAsrDictation.Hotkeys;

public readonly record struct HotkeyEventData(
    int VirtualKey,
    int ScanCode,
    bool IsExtendedKey,
    bool IsKeyDown,
    bool IsAltContext)
{
    public bool IsModifier =>
        VirtualKey is 0x10 or
            0x11 or
            0x12 or
            0x5B or
            0x5C or
            0xA0 or
            0xA1 or
            0xA2 or
            0xA3 or
            0xA4 or
            0xA5;
}
