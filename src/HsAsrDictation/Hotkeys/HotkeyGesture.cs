namespace HsAsrDictation.Hotkeys;

public sealed class HotkeyGesture
{
    public HotkeyPhysicalKey[] Keys { get; init; } = [];

    public bool IsValid => IsValidKeys(HotkeyPhysicalKeySet.Normalize(Keys));

    public static HotkeyGesture CreateDefault() => new()
    {
        Keys = [HotkeyPhysicalKey.RightAlt]
    };

    public HotkeyGesture CreateCopy() => new()
    {
        Keys = [.. Keys]
    };

    public HotkeyGesture Normalize()
    {
        var normalizedKeys = HotkeyPhysicalKeySet.Normalize(Keys);
        if (!IsValidKeys(normalizedKeys))
        {
            return CreateDefault();
        }

        return new HotkeyGesture
        {
            Keys = [.. normalizedKeys]
        };
    }

    public bool IsEquivalentTo(HotkeyGesture? other)
    {
        if (other is null)
        {
            return false;
        }

        var normalized = Normalize();
        var otherNormalized = other.Normalize();
        return HotkeyPhysicalKeySet.SetEquals(normalized.Keys, otherNormalized.Keys);
    }

    public string ToDisplayText()
    {
        var normalized = Normalize();
        return HotkeyPhysicalKeySet.FormatDisplayText(normalized.Keys);
    }

    private static bool IsValidKeys(IReadOnlyList<HotkeyPhysicalKey> keys)
    {
        return keys.Count > 0 &&
               !(keys.Count == 1 && (keys[0].IsEscapeKey || keys[0].IsWindowsKey));
    }
}
