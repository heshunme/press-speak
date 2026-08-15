namespace HsAsrDictation.Hotkeys;

public static class HotkeyActivationEvaluator
{
    public static bool IsActive(HotkeyGesture gesture, IEnumerable<HotkeyPhysicalKey> pressedKeys)
    {
        var normalizedGestureKeys = gesture.Normalize().Keys;
        return IsActive(normalizedGestureKeys, ToPressedKeySet(pressedKeys));
    }

    /// <summary>
    /// 零分配热路径重载：normalizedGestureKeys 必须是 HotkeyGesture.Normalize 后的按键，
    /// pressedKeys 必须元素有效且不重复（HotkeyPressedState 的按键集合满足该约定）。
    /// </summary>
    public static bool IsActive(HotkeyPhysicalKey[] normalizedGestureKeys, HashSet<HotkeyPhysicalKey> pressedKeys)
    {
        // 与 HotkeyPhysicalKeySet.Normalize 一致：按下侧含 Right Alt 时，忽略 AltGr 附带的 Left Ctrl。
        var ignoreCompanionLeftControl = HotkeyPhysicalKeySet.AnyRightAltKey(pressedKeys);
        var matchedCount = 0;
        foreach (var key in pressedKeys)
        {
            if (ignoreCompanionLeftControl && key.IsLeftControlKey)
            {
                continue;
            }

            if (!HotkeyPhysicalKeySet.ContainsKey(normalizedGestureKeys, key))
            {
                return false;
            }

            matchedCount++;
        }

        return matchedCount == normalizedGestureKeys.Length;
    }

    public static bool IsSubsetMatch(HotkeyGesture gesture, IEnumerable<HotkeyPhysicalKey> pressedKeys)
    {
        var normalizedGestureKeys = gesture.Normalize().Keys;
        var normalizedPressedKeys = NormalizePressedKeys(normalizedGestureKeys, pressedKeys);
        return IsSubsetMatch(normalizedGestureKeys, normalizedPressedKeys);
    }

    /// <summary>零分配热路径重载，输入约定同 IsActive(HotkeyPhysicalKey[], HashSet)。</summary>
    public static bool IsSubsetMatch(HotkeyPhysicalKey[] normalizedGestureKeys, HashSet<HotkeyPhysicalKey> pressedKeys)
    {
        // 手势与按下侧都含 Right Alt 时，忽略 AltGr 附带的 Left Ctrl。
        var ignoreCompanionLeftControl =
            HotkeyPhysicalKeySet.AnyRightAltKey(normalizedGestureKeys) &&
            HotkeyPhysicalKeySet.AnyRightAltKey(pressedKeys);
        var anyKey = false;
        foreach (var key in pressedKeys)
        {
            if (ignoreCompanionLeftControl && key.IsLeftControlKey)
            {
                continue;
            }

            if (!HotkeyPhysicalKeySet.ContainsKey(normalizedGestureKeys, key))
            {
                return false;
            }

            anyKey = true;
        }

        return anyKey;
    }

    private static HashSet<HotkeyPhysicalKey> NormalizePressedKeys(
        HotkeyPhysicalKey[] normalizedGestureKeys,
        IEnumerable<HotkeyPhysicalKey> pressedKeys)
    {
        var normalizedPressedKeys = new HashSet<HotkeyPhysicalKey>(pressedKeys);

        if (ShouldIgnoreAltGrCompanionLeftControl(normalizedGestureKeys, normalizedPressedKeys))
        {
            normalizedPressedKeys.RemoveWhere(static key => key.IsLeftControlKey);
        }

        return normalizedPressedKeys;
    }

    private static bool ShouldIgnoreAltGrCompanionLeftControl(
        HotkeyPhysicalKey[] normalizedGestureKeys,
        HashSet<HotkeyPhysicalKey> normalizedPressedKeys) =>
        HotkeyPhysicalKeySet.AnyRightAltKey(normalizedGestureKeys) &&
        HotkeyPhysicalKeySet.AnyRightAltKey(normalizedPressedKeys);

    private static HashSet<HotkeyPhysicalKey> ToPressedKeySet(IEnumerable<HotkeyPhysicalKey> pressedKeys)
    {
        var pressedKeySet = new HashSet<HotkeyPhysicalKey>();
        foreach (var key in pressedKeys)
        {
            if (key.IsValid)
            {
                pressedKeySet.Add(key);
            }
        }

        return pressedKeySet;
    }
}
