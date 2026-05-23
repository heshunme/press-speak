namespace HsAsrDictation.Hotkeys;

public sealed class HotkeyPressedState
{
    private readonly HashSet<HotkeyPhysicalKey> _pressedKeys = [];

    public IReadOnlyCollection<HotkeyPhysicalKey> PressedKeys => _pressedKeys;

    public bool IsEmpty => _pressedKeys.Count == 0;

    public void Clear() => _pressedKeys.Clear();

    public void Apply(HotkeyEventData keyEvent)
    {
        if (!keyEvent.TryGetPhysicalKey(out var key))
        {
            return;
        }

        if (keyEvent.IsKeyDown)
        {
            _pressedKeys.Add(key);
        }
        else
        {
            _pressedKeys.Remove(key);
        }
    }
}
