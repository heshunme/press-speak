using System.Text;
using HsAsrDictation.Interop;

namespace HsAsrDictation.Hotkeys;

public readonly record struct HotkeyPhysicalKey(int VirtualKey, int ScanCode, bool IsExtendedKey)
{
    public static HotkeyPhysicalKey RightAlt => new(0xA5, 0x38, true);

    public bool IsValid => VirtualKey > 0 && ScanCode > 0;

    public bool IsEscapeKey => VirtualKey == 0x1B || (ScanCode == 0x01 && !IsExtendedKey);

    public bool IsWindowsKey => VirtualKey is 0x5B or 0x5C;

    public bool IsModifierKey => VirtualKey is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5
        || IsWindowsKey;

    public bool IsRightAltKey => VirtualKey == 0xA5 || (ScanCode == 0x38 && IsExtendedKey);

    public bool IsLeftControlKey => VirtualKey == 0xA2 || (VirtualKey == 0x11 && ScanCode == 0x1D && !IsExtendedKey);

    public string ToDisplayText()
    {
        if (OperatingSystem.IsWindows())
        {
            var buffer = new StringBuilder(64);
            var lParam = ScanCode << 16;
            if (IsExtendedKey)
            {
                lParam |= 1 << 24;
            }

            if (Win32.GetKeyNameText(lParam, buffer, buffer.Capacity) > 0)
            {
                return buffer.ToString();
            }
        }

        return GetFallbackDisplayText();
    }

    public static bool TryCreate(HotkeyEventData keyEvent, out HotkeyPhysicalKey key)
    {
        if (keyEvent.VirtualKey <= 0 || keyEvent.ScanCode <= 0)
        {
            key = default;
            return false;
        }

        key = new HotkeyPhysicalKey(keyEvent.VirtualKey, keyEvent.ScanCode, keyEvent.IsExtendedKey);
        return true;
    }

    private string GetFallbackDisplayText()
    {
        if (IsRightAltKey)
        {
            return "Right Alt";
        }

        if (IsLeftControlKey)
        {
            return "Left Ctrl";
        }

        return VirtualKey switch
        {
            0xA4 => "Left Alt",
            0xA3 => "Right Ctrl",
            0xA0 => "Left Shift",
            0xA1 => "Right Shift",
            0x5B => "Left Win",
            0x5C => "Right Win",
            0x20 => "Space",
            0x2E => "Delete",
            >= 0x30 and <= 0x39 => ((char)VirtualKey).ToString(),
            >= 0x41 and <= 0x5A => ((char)VirtualKey).ToString(),
            >= 0x70 and <= 0x7B => $"F{VirtualKey - 0x6F}",
            _ => $"VK_{VirtualKey:X2}"
        };
    }
}
