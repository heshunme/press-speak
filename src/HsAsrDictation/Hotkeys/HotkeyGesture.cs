using System.Text;
using System.Windows.Input;
using HsAsrDictation.Interop;

namespace HsAsrDictation.Hotkeys;

public sealed class HotkeyGesture
{
    public HotkeyModifiers Modifiers { get; init; } = HotkeyModifiers.Alt;

    public Key Key { get; init; } = Key.Oem3;

    public int VirtualKey { get; init; }

    public int PrimaryScanCode { get; init; }

    public bool IsExtendedKey { get; init; }

    public HotkeyGesture CreateCopy() => new()
    {
        Modifiers = Modifiers,
        Key = Key,
        VirtualKey = VirtualKey,
        PrimaryScanCode = PrimaryScanCode,
        IsExtendedKey = IsExtendedKey
    };

    public HotkeyGesture Normalize()
    {
        var virtualKey = VirtualKey != 0 ? VirtualKey : KeyInterop.VirtualKeyFromKey(Key);
        if (virtualKey == 0)
        {
            virtualKey = 0xC0;
        }

        var key = Key != Key.None
            ? Key
            : KeyInterop.KeyFromVirtualKey(virtualKey);
        var primaryScanCode = PrimaryScanCode != 0
            ? PrimaryScanCode
            : unchecked((int)Win32.MapVirtualKey((uint)virtualKey, Win32.MAPVK_VK_TO_VSC));

        return new HotkeyGesture
        {
            Modifiers = Modifiers,
            Key = key,
            VirtualKey = virtualKey,
            PrimaryScanCode = primaryScanCode,
            IsExtendedKey = IsExtendedKey || IsExtendedVirtualKey(virtualKey)
        };
    }

    public bool IsEquivalentTo(HotkeyGesture? other)
    {
        if (other is null)
        {
            return false;
        }

        var normalized = Normalize();
        var otherNormalized = other.Normalize();
        return normalized.Modifiers == otherNormalized.Modifiers &&
               normalized.VirtualKey == otherNormalized.VirtualKey &&
               normalized.PrimaryScanCode == otherNormalized.PrimaryScanCode &&
               normalized.IsExtendedKey == otherNormalized.IsExtendedKey;
    }

    public HotkeyBindingSnapshot ToBinding()
    {
        var normalized = Normalize();
        return new HotkeyBindingSnapshot(
            normalized.Modifiers,
            normalized.VirtualKey,
            normalized.PrimaryScanCode,
            normalized.IsExtendedKey);
    }

    public string ToDisplayText()
    {
        var normalized = Normalize();
        var parts = new List<string>();
        if (normalized.Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (normalized.Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (normalized.Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (normalized.Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(FormatPrimaryKey(normalized));
        return string.Join(" + ", parts);
    }

    private static string FormatPrimaryKey(HotkeyGesture gesture)
    {
        if (gesture.PrimaryScanCode > 0)
        {
            var lParam = gesture.PrimaryScanCode << 16;
            if (gesture.IsExtendedKey)
            {
                lParam |= 1 << 24;
            }

            var buffer = new StringBuilder(64);
            if (Win32.GetKeyNameText(lParam, buffer, buffer.Capacity) > 0)
            {
                return buffer.ToString();
            }
        }

        return gesture.Key != Key.None
            ? gesture.Key.ToString()
            : $"VK_{gesture.VirtualKey:X2}";
    }

    private static bool IsExtendedVirtualKey(int virtualKey) =>
        virtualKey is 0x21 or
            0x22 or
            0x23 or
            0x24 or
            0x25 or
            0x26 or
            0x27 or
            0x28 or
            0x2D or
            0x2E or
            0x6F or
            0xA3 or
            0xA5;
}
