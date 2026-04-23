using System.Windows.Input;

namespace HsAsrDictation.Hotkeys;

public static class HotkeyCaptureParser
{
    public static bool TryCreateGesture(
        HotkeyEventData keyEvent,
        HotkeyModifiers modifiers,
        out HotkeyGesture? gesture,
        out HotkeyCaptureFailureReason failureReason)
    {
        if (!HotkeyCaptureEvaluator.TryCreateCandidate(
                keyEvent,
                modifiers,
                out var candidate,
                out failureReason))
        {
            gesture = null;
            return false;
        }

        gesture = new HotkeyGesture
        {
            Modifiers = candidate.Modifiers,
            Key = KeyInterop.KeyFromVirtualKey(candidate.VirtualKey),
            VirtualKey = candidate.VirtualKey,
            PrimaryScanCode = candidate.ScanCode,
            IsExtendedKey = candidate.IsExtendedKey
        };
        return true;
    }

    public static Key ResolveKey(Key key, Key systemKey) =>
        key == Key.System && systemKey != Key.None
            ? systemKey
            : key;

    public static int ResolveVirtualKey(Key key, Key systemKey)
    {
        var resolvedKey = ResolveKey(key, systemKey);
        return resolvedKey is Key.None or Key.System
            ? 0
            : KeyInterop.VirtualKeyFromKey(resolvedKey);
    }

    public static HotkeyModifiers ToHotkeyModifiers(ModifierKeys modifierKeys)
    {
        var modifiers = HotkeyModifiers.None;

        if (modifierKeys.HasFlag(ModifierKeys.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (modifierKeys.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (modifierKeys.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (modifierKeys.HasFlag(ModifierKeys.Windows))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        return modifiers;
    }

    public static string FormatModifierText(ModifierKeys modifierKeys) =>
        HotkeyCaptureEvaluator.FormatModifierText(ToHotkeyModifiers(modifierKeys));

    public static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or
            Key.RightCtrl or
            Key.LeftAlt or
            Key.RightAlt or
            Key.LeftShift or
            Key.RightShift or
            Key.LWin or
            Key.RWin;
}
