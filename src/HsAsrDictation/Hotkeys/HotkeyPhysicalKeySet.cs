namespace HsAsrDictation.Hotkeys;

public static class HotkeyPhysicalKeySet
{
    public static IReadOnlyList<HotkeyPhysicalKey> Normalize(IEnumerable<HotkeyPhysicalKey> keys)
    {
        var orderedKeys = new List<HotkeyPhysicalKey>();
        var seen = new HashSet<HotkeyPhysicalKey>();

        foreach (var key in keys)
        {
            if (!key.IsValid || !seen.Add(key))
            {
                continue;
            }

            orderedKeys.Add(key);
        }

        // Treat AltGr's companion Left Ctrl as part of a physical Right Alt press.
        if (orderedKeys.Exists(static key => key.IsRightAltKey))
        {
            orderedKeys.RemoveAll(static key => key.IsLeftControlKey);
        }

        return orderedKeys;
    }

    public static bool SetEquals(IEnumerable<HotkeyPhysicalKey> left, IEnumerable<HotkeyPhysicalKey> right)
    {
        var normalizedLeft = Normalize(left);
        var normalizedRight = Normalize(right);
        if (normalizedLeft.Count != normalizedRight.Count)
        {
            return false;
        }

        var rightSet = new HashSet<HotkeyPhysicalKey>(normalizedRight);
        return normalizedLeft.All(rightSet.Contains);
    }

    public static string FormatDisplayText(IEnumerable<HotkeyPhysicalKey> keys)
    {
        var normalized = Normalize(keys);
        return string.Join(" + ", normalized.Select(static key => key.ToDisplayText()));
    }
}
