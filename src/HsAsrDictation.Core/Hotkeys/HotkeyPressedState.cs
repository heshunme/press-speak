namespace HsAsrDictation.Hotkeys;

public sealed class HotkeyPressedState
{
    private readonly HashSet<HotkeyPhysicalKey> _pressedKeys = [];

    public IReadOnlyCollection<HotkeyPhysicalKey> PressedKeys => _pressedKeys;

    /// <summary>热路径零分配评估使用的底层集合（元素有效且不重复）；调用方只读，不得修改。</summary>
    internal HashSet<HotkeyPhysicalKey> PressedKeySet => _pressedKeys;

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
