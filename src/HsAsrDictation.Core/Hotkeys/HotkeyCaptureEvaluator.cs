namespace HsAsrDictation.Hotkeys;

public enum HotkeyCaptureFailureReason
{
    None = 0,
    NoKeys = 1,
    EscapeReserved = 2,
    SingleWindowsKeyNotAllowed = 3
}

public readonly record struct HotkeyCaptureCandidate(HotkeyPhysicalKey[] Keys);

public static class HotkeyCaptureEvaluator
{
    public static bool TryCreateCandidate(
        IEnumerable<HotkeyPhysicalKey> keys,
        out HotkeyCaptureCandidate candidate,
        out HotkeyCaptureFailureReason failureReason)
    {
        var normalizedKeys = HotkeyPhysicalKeySet.Normalize(keys);
        if (normalizedKeys.Count == 0)
        {
            candidate = default;
            failureReason = HotkeyCaptureFailureReason.NoKeys;
            return false;
        }

        if (normalizedKeys.Count == 1 && normalizedKeys[0].IsEscapeKey)
        {
            candidate = default;
            failureReason = HotkeyCaptureFailureReason.EscapeReserved;
            return false;
        }

        if (normalizedKeys.Count == 1 && normalizedKeys[0].IsWindowsKey)
        {
            candidate = default;
            failureReason = HotkeyCaptureFailureReason.SingleWindowsKeyNotAllowed;
            return false;
        }

        candidate = new HotkeyCaptureCandidate([.. normalizedKeys]);
        failureReason = HotkeyCaptureFailureReason.None;
        return true;
    }

    public static string FormatKeyText(IEnumerable<HotkeyPhysicalKey> keys) =>
        HotkeyPhysicalKeySet.FormatDisplayText(keys);
}
