using System.Windows.Input;

namespace HsAsrDictation.Hotkeys;

public sealed class HotkeyGesture
{
    public HotkeyModifiers Modifiers { get; init; } = HotkeyModifiers.Alt;

    public Key Key { get; init; } = Key.Oem3;

    public HotkeyGesture CreateCopy() => new()
    {
        Modifiers = Modifiers,
        Key = Key
    };

    public bool IsEquivalentTo(HotkeyGesture? other) =>
        other is not null &&
        Modifiers == other.Modifiers &&
        Key == other.Key;

    public string ToDisplayText()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(Key.ToString());
        return string.Join(" + ", parts);
    }
}
