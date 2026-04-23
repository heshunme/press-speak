using System.Diagnostics;
using System.Runtime.InteropServices;
using HsAsrDictation.Interop;
using HsAsrDictation.Logging;

namespace HsAsrDictation.Hotkeys;

public sealed class LowLevelKeyboardHotkeyManager : IHotkeyManager
{
    private readonly LocalLogService _logger;
    private readonly HotkeyPressedState _pressedState = new();
    private readonly Win32.LowLevelKeyboardProc _hookCallback;
    private IntPtr _hookHandle = IntPtr.Zero;
    private bool _gestureActive;
    private bool _isSuspended;

    public LowLevelKeyboardHotkeyManager(LocalLogService logger)
    {
        _logger = logger;
        _hookCallback = HookCallback;
        CurrentGesture = new HotkeyGesture();
    }

    public event EventHandler? Pressed;

    public event EventHandler? Released;

    public HotkeyGesture CurrentGesture { get; private set; }

    public void Start(HotkeyGesture gesture)
    {
        CurrentGesture = gesture.Normalize();

        if (_hookHandle != IntPtr.Zero)
        {
            return;
        }

        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule
            ?? throw new InvalidOperationException("无法获取当前进程模块。");

        var moduleHandle = Win32.GetModuleHandle(currentModule.ModuleName);
        _hookHandle = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _hookCallback, moduleHandle, 0);

        if (_hookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("注册全局键盘钩子失败。");
        }

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
        if (_hookHandle != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            if (_isSuspended)
            {
                return Win32.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            var hookStruct = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            var message = wParam.ToInt32();
            var keyEvent = CreateKeyEvent(hookStruct, message);
            _pressedState.Apply(keyEvent);

            var nowActive = HotkeyActivationEvaluator.IsActive(CurrentGesture.ToBinding(), _pressedState, keyEvent);
            if (nowActive && !_gestureActive)
            {
                _gestureActive = true;
                Pressed?.Invoke(this, EventArgs.Empty);
            }
            else if (!nowActive && _gestureActive)
            {
                _gestureActive = false;
                Released?.Invoke(this, EventArgs.Empty);
            }
        }

        return Win32.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
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

    private static HotkeyEventData CreateKeyEvent(Win32.KBDLLHOOKSTRUCT hookStruct, int message)
    {
        return new HotkeyEventData(
            unchecked((int)hookStruct.vkCode),
            unchecked((int)hookStruct.scanCode),
            (hookStruct.flags & Win32.LLKHF_EXTENDED) != 0,
            message is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN,
            (hookStruct.flags & Win32.LLKHF_ALTDOWN) != 0);
    }
}
