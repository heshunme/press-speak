namespace HsAsrDictation.Hotkeys;

internal static class HotkeySuppressionEvaluator
{
    public static bool ShouldSuppress(
        HotkeyGesture gesture,
        IReadOnlyCollection<HotkeyPhysicalKey> pressedKeys,
        HotkeyEventData keyEvent,
        bool wasGestureActive,
        bool isGestureActive)
    {
        if (!keyEvent.TryGetPhysicalKey(out var key))
        {
            return false;
        }

        var normalizedGesture = gesture.Normalize();
        if (!normalizedGesture.Keys.Contains(key))
        {
            return false;
        }

        if (normalizedGesture.Keys.Length == 1)
        {
            return true;
        }

        if (key.IsModifierKey)
        {
            return false;
        }

        return wasGestureActive ||
               isGestureActive ||
               HotkeyActivationEvaluator.IsSubsetMatch(normalizedGesture, pressedKeys);
    }
}
