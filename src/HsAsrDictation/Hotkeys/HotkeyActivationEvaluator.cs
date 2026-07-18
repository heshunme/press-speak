namespace HsAsrDictation.Hotkeys;

public static class HotkeyActivationEvaluator
{
    public static bool IsActive(HotkeyGesture gesture, IEnumerable<HotkeyPhysicalKey> pressedKeys)
    {
        var normalizedGesture = gesture.Normalize();
        return HotkeyPhysicalKeySet.SetEquals(normalizedGesture.Keys, pressedKeys);
    }

    public static bool IsSubsetMatch(HotkeyGesture gesture, IEnumerable<HotkeyPhysicalKey> pressedKeys)
    {
        var normalizedGesture = gesture.Normalize();
        var normalizedPressedKeys = NormalizePressedKeys(normalizedGesture, pressedKeys);
        if (normalizedPressedKeys.Count == 0)
        {
            return false;
        }

        return normalizedPressedKeys.All(normalizedGesture.Keys.Contains);
    }

    private static IReadOnlyCollection<HotkeyPhysicalKey> NormalizePressedKeys(
        HotkeyGesture normalizedGesture,
        IEnumerable<HotkeyPhysicalKey> pressedKeys)
    {
        var normalizedPressedKeys = pressedKeys.ToHashSet();

        if (ShouldIgnoreAltGrCompanionLeftControl(normalizedGesture, normalizedPressedKeys))
        {
            normalizedPressedKeys.RemoveWhere(key => key.IsLeftControlKey);
        }

        return normalizedPressedKeys;
    }

    private static bool ShouldIgnoreAltGrCompanionLeftControl(
        HotkeyGesture normalizedGesture,
        IReadOnlyCollection<HotkeyPhysicalKey> normalizedPressedKeys) =>
        normalizedGesture.Keys.Any(key => key.IsRightAltKey) &&
        normalizedPressedKeys.Any(key => key.IsRightAltKey) &&
        normalizedPressedKeys.Any(key => key.IsLeftControlKey);
}
