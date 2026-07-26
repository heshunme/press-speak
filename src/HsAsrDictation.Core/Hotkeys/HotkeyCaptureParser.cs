namespace HsAsrDictation.Hotkeys;

public static class HotkeyCaptureParser
{
    public static bool TryCreateGesture(
        IEnumerable<HotkeyPhysicalKey> keys,
        out HotkeyGesture? gesture,
        out HotkeyCaptureFailureReason failureReason)
    {
        if (!HotkeyCaptureEvaluator.TryCreateCandidate(
                keys,
                out var candidate,
                out failureReason))
        {
            gesture = null;
            return false;
        }

        gesture = new HotkeyGesture
        {
            Keys = [.. candidate.Keys]
        };
        return true;
    }
}
