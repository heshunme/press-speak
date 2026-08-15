using System.Collections.Concurrent;
using HsAsrDictation.Logging;

namespace HsAsrDictation.Hotkeys;

public sealed class LowLevelKeyboardHotkeyManager : IHotkeyManager
{
    private readonly LowLevelKeyboardEventSource _eventSource;
    private readonly LocalLogService _logger;
    private readonly HotkeyPressedState _pressedState = new();

    // 同步吞键路径为每个事件产出的评估结果；后台派发线程按 FIFO 取回，补发日志与 Pressed/Released。
    private readonly ConcurrentQueue<HotkeyEventOutcome> _eventOutcomes = new();

    // 归一化手势按键缓存：只在 Start/UpdateGesture 时整体替换，钩子热路径不再重复归一化。
    private volatile HotkeyPhysicalKey[] _normalizedGestureKeys;
    private volatile bool _gestureActive;
    private volatile bool _isSuspended;

    public LowLevelKeyboardHotkeyManager(LowLevelKeyboardEventSource eventSource, LocalLogService logger)
    {
        _eventSource = eventSource;
        _logger = logger;
        _eventSource.KeyEvent += OnKeyEvent;
        _eventSource.ShouldSuppressKeyEvent = ShouldSuppressKeyEvent;
        CurrentGesture = HotkeyGesture.CreateDefault();
        _normalizedGestureKeys = CurrentGesture.Normalize().Keys;
    }

    public event EventHandler? Pressed;

    public event EventHandler? Released;

    public HotkeyGesture CurrentGesture { get; private set; }

    public void Start(HotkeyGesture gesture)
    {
        SetCurrentGesture(gesture);
        _eventSource.Start();
        _logger.Info($"热键已启用：{CurrentGesture.ToDisplayText()}");
    }

    public void UpdateGesture(HotkeyGesture gesture)
    {
        SetCurrentGesture(gesture);
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

    private void SetCurrentGesture(HotkeyGesture gesture)
    {
        var normalized = gesture.Normalize();
        CurrentGesture = normalized;
        _normalizedGestureKeys = normalized.Keys;
    }

    private void OnKeyEvent(object? sender, HotkeyEventData keyEvent)
    {
        // 与同步吞键路径一一配对；事件源按序派发，正常流程下出队必然成功。
        if (!_eventOutcomes.TryDequeue(out var outcome))
        {
            _logger.Warn($"热键事件缺少同步评估结果，已忽略：{FormatEventData(keyEvent)}");
            return;
        }

        if (outcome.Transition == HotkeyTransition.Activated)
        {
            _logger.Info($"热键按下已命中：{FormatEventData(keyEvent)} | binding={CurrentGesture.ToDisplayText()}");
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        else if (outcome.Transition == HotkeyTransition.Deactivated)
        {
            _logger.Info($"热键已释放：{FormatEventData(keyEvent)} | binding={CurrentGesture.ToDisplayText()}");
            Released?.Invoke(this, EventArgs.Empty);
        }

        if (outcome.Suppressed)
        {
            _logger.Info($"热键事件已消费：{FormatEventData(keyEvent)} | binding={CurrentGesture.ToDisplayText()}");
        }
    }

    private bool ShouldSuppressKeyEvent(HotkeyEventData keyEvent)
    {
        // 在低级键盘钩子回调里同步执行：只做集合运算，日志与事件通知都留给后台派发。
        if (_isSuspended)
        {
            _eventOutcomes.Enqueue(HotkeyEventOutcome.None);
            return false;
        }

        var wasActive = _gestureActive;
        _pressedState.Apply(keyEvent);

        var gestureKeys = _normalizedGestureKeys;
        var pressedKeys = _pressedState.PressedKeySet;
        var nowActive = HotkeyActivationEvaluator.IsActive(gestureKeys, pressedKeys);
        var transition = nowActive == wasActive
            ? HotkeyTransition.None
            : nowActive
                ? HotkeyTransition.Activated
                : HotkeyTransition.Deactivated;
        _gestureActive = nowActive;

        var suppress = HotkeySuppressionEvaluator.ShouldSuppress(
            gestureKeys,
            pressedKeys,
            keyEvent,
            wasActive,
            nowActive);

        _eventOutcomes.Enqueue(new HotkeyEventOutcome(transition, suppress));
        return suppress;
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

    private enum HotkeyTransition
    {
        None,
        Activated,
        Deactivated
    }

    private readonly record struct HotkeyEventOutcome(HotkeyTransition Transition, bool Suppressed)
    {
        public static HotkeyEventOutcome None { get; } = new(HotkeyTransition.None, false);
    }
}
