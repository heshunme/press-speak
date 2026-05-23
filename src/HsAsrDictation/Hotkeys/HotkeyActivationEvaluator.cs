namespace HsAsrDictation.Hotkeys;

public static class HotkeyActivationEvaluator
{
    public static bool IsActive(HotkeyGesture gesture, IEnumerable<HotkeyPhysicalKey> pressedKeys)
    {
        var normalizedGesture = gesture.Normalize();
        return HotkeyPhysicalKeySet.SetEquals(normalizedGesture.Keys, pressedKeys);
    }
}
