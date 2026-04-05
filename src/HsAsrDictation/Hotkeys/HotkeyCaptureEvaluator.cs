namespace HsAsrDictation.Hotkeys;

public enum HotkeyCaptureFailureReason
{
    None = 0,
    MissingModifier = 1,
    MissingPrimaryKey = 2,
    InvalidPrimaryKey = 3
}

public readonly record struct HotkeyCaptureCandidate(HotkeyModifiers Modifiers, int VirtualKey);

public static class HotkeyCaptureEvaluator
{
    public static bool TryCreateCandidate(
        int virtualKey,
        HotkeyModifiers modifiers,
        out HotkeyCaptureCandidate candidate,
        out HotkeyCaptureFailureReason failureReason)
    {
        if (modifiers == HotkeyModifiers.None)
        {
            candidate = default;
            failureReason = HotkeyCaptureFailureReason.MissingModifier;
            return false;
        }

        if (virtualKey == 0)
        {
            candidate = default;
            failureReason = HotkeyCaptureFailureReason.MissingPrimaryKey;
            return false;
        }

        if (IsModifierVirtualKey(virtualKey))
        {
            candidate = default;
            failureReason = HotkeyCaptureFailureReason.MissingPrimaryKey;
            return false;
        }

        if (!IsValidPrimaryVirtualKey(virtualKey))
        {
            candidate = default;
            failureReason = HotkeyCaptureFailureReason.InvalidPrimaryKey;
            return false;
        }

        candidate = new HotkeyCaptureCandidate(modifiers, virtualKey);
        failureReason = HotkeyCaptureFailureReason.None;
        return true;
    }

    public static string FormatModifierText(HotkeyModifiers modifiers)
    {
        var parts = new List<string>();

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        return string.Join(" + ", parts);
    }

    public static bool IsModifierVirtualKey(int virtualKey) =>
        virtualKey is 0x10 or
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

    public static bool IsValidPrimaryVirtualKey(int virtualKey) =>
        virtualKey != 0x1B &&
        !IsModifierVirtualKey(virtualKey);
}
