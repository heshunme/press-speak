namespace HsAsrDictation.Hotkeys;

public interface IHotkeyManager : IDisposable
{
    event EventHandler? Pressed;

    event EventHandler? Released;

    HotkeyGesture CurrentGesture { get; }

    void Start(HotkeyGesture gesture);

    void UpdateGesture(HotkeyGesture gesture);

    void Suspend();

    void Resume();
}
