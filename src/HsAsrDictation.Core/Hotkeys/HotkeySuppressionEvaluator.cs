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
        var normalizedGestureKeys = gesture.Normalize().Keys;
        var pressedKeySet = pressedKeys as HashSet<HotkeyPhysicalKey> ?? new HashSet<HotkeyPhysicalKey>(pressedKeys);
        return ShouldSuppress(normalizedGestureKeys, pressedKeySet, keyEvent, wasGestureActive, isGestureActive);
    }

    /// <summary>
    /// 零分配热路径重载：normalizedGestureKeys 必须是 HotkeyGesture.Normalize 后的按键，
    /// pressedKeys 必须元素有效且不重复（HotkeyPressedState 的按键集合满足该约定）。
    /// </summary>
    public static bool ShouldSuppress(
        HotkeyPhysicalKey[] normalizedGestureKeys,
        HashSet<HotkeyPhysicalKey> pressedKeys,
        HotkeyEventData keyEvent,
        bool wasGestureActive,
        bool isGestureActive)
    {
        if (!keyEvent.TryGetPhysicalKey(out var key))
        {
            return false;
        }

        if (!HotkeyPhysicalKeySet.ContainsKey(normalizedGestureKeys, key))
        {
            return false;
        }

        if (normalizedGestureKeys.Length == 1)
        {
            return true;
        }

        if (key.IsModifierKey)
        {
            return false;
        }

        return wasGestureActive ||
               isGestureActive ||
               HotkeyActivationEvaluator.IsSubsetMatch(normalizedGestureKeys, pressedKeys);
    }
}
