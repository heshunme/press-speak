using HsAsrDictation.Logging;

namespace HsAsrDictation.Hotkeys;

public sealed class LowLevelKeyboardHotkeyManager : IHotkeyManager
{
    private readonly LowLevelKeyboardEventSource _eventSource;
    private readonly LocalLogService _logger;
    private readonly HotkeyPressedState _pressedState = new();
    private bool _gestureActive;
    private bool _isSuspended;
    private bool _suppressCurrentKeyEvent;

    public LowLevelKeyboardHotkeyManager(LowLevelKeyboardEventSource eventSource, LocalLogService logger)
    {
        _eventSource = eventSource;
        _logger = logger;
        _eventSource.KeyEvent += OnKeyEvent;
        _eventSource.ShouldSuppressKeyEvent = ShouldSuppressKeyEvent;
        CurrentGesture = HotkeyGesture.CreateDefault();
    }

    public event EventHandler? Pressed;

    public event EventHandler? Released;

    public HotkeyGesture CurrentGesture { get; private set; }

    public void Start(HotkeyGesture gesture)
    {
        CurrentGesture = gesture.Normalize();
        _eventSource.Start();
        _logger.Info($"热键已启用：{CurrentGesture.ToDisplayText()}");
    }

    public void UpdateGesture(HotkeyGesture gesture)
    {
        CurrentGesture = gesture.Normalize();
        ResetState();
        _logger.Info($"热键已更新：{CurrentGesture.ToDisplayText()}");
    }

    public void Suspend()
    {
        if (_isSuspended)
        {
            return;
        }

        _isSuspended = true;
        ResetState(emitRelease: true);
        _logger.Info("热键监听已暂停。");
    }

    public void Resume()
    {
        if (!_isSuspended)
        {
            return;
        }

        ResetState();
        _isSuspended = false;
        _logger.Info($"热键监听已恢复：{CurrentGesture.ToDisplayText()}");
    }

    public void Dispose()
    {
        _eventSource.KeyEvent -= OnKeyEvent;
        _eventSource.ShouldSuppressKeyEvent = null;
    }

    private void OnKeyEvent(object? sender, HotkeyEventData keyEvent)
    {
        if (_isSuspended)
        {
            _suppressCurrentKeyEvent = false;
            return;
        }

        var wasActive = _gestureActive;
        _pressedState.Apply(keyEvent);

        var nowActive = HotkeyActivationEvaluator.IsActive(CurrentGesture, _pressedState.PressedKeys);
        if (nowActive && !_gestureActive)
        {
            _gestureActive = true;
            _logger.Info($"热键按下已命中：{FormatEventData(keyEvent)} | binding={CurrentGesture.ToDisplayText()}");
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        else if (!nowActive && _gestureActive)
        {
            _gestureActive = false;
            _logger.Info($"热键已释放：{FormatEventData(keyEvent)} | binding={CurrentGesture.ToDisplayText()}");
            Released?.Invoke(this, EventArgs.Empty);
        }

        _suppressCurrentKeyEvent = HotkeySuppressionEvaluator.ShouldSuppress(
            CurrentGesture,
            _pressedState.PressedKeys,
            keyEvent,
            wasActive,
            nowActive);
    }

    private void ResetState(bool emitRelease = false)
    {
        var wasActive = _gestureActive;
        _pressedState.Clear();
        _gestureActive = false;

        if (emitRelease && wasActive)
        {
            Released?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string FormatEventData(HotkeyEventData keyEvent) =>
        $"vk=0x{keyEvent.VirtualKey:X2}, scan=0x{keyEvent.ScanCode:X2}, extended={keyEvent.IsExtendedKey}, altContext={keyEvent.IsAltContext}, injected={keyEvent.IsInjected}, keyDown={keyEvent.IsKeyDown}";

    private bool ShouldSuppressKeyEvent(HotkeyEventData keyEvent)
    {
        var suppress = _suppressCurrentKeyEvent;
        if (suppress)
        {
            _logger.Info($"热键事件已消费：{FormatEventData(keyEvent)} | binding={CurrentGesture.ToDisplayText()}");
        }

        _suppressCurrentKeyEvent = false;
        return suppress;
    }
}
