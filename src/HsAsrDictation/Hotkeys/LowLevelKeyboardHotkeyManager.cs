using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using HsAsrDictation.Interop;
using HsAsrDictation.Logging;

namespace HsAsrDictation.Hotkeys;

public sealed class LowLevelKeyboardHotkeyManager : IHotkeyManager
{
    private readonly HashSet<int> _pressedKeys = [];
    private readonly LocalLogService _logger;
    private readonly Win32.LowLevelKeyboardProc _hookCallback;
    private IntPtr _hookHandle = IntPtr.Zero;
    private bool _gestureActive;

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
        CurrentGesture = gesture;

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

        _logger.Info($"热键已启用：{gesture.ToDisplayText()}");
    }

    public void UpdateGesture(HotkeyGesture gesture)
    {
        CurrentGesture = gesture;
        _pressedKeys.Clear();
        _gestureActive = false;
        _logger.Info($"热键已更新：{gesture.ToDisplayText()}");
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
            var hookStruct = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            var message = wParam.ToInt32();

            if (message is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN)
            {
                _pressedKeys.Add((int)hookStruct.vkCode);
            }
            else if (message is Win32.WM_KEYUP or Win32.WM_SYSKEYUP)
            {
                _pressedKeys.Remove((int)hookStruct.vkCode);
            }

            var nowActive = IsGestureActive();
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

    private bool IsGestureActive()
    {
        var keyCode = KeyInterop.VirtualKeyFromKey(CurrentGesture.Key);
        if (!_pressedKeys.Contains(keyCode))
        {
            return false;
        }

        return ModifierActive(HotkeyModifiers.Control, 0x11, 0xA2, 0xA3) &&
               ModifierActive(HotkeyModifiers.Alt, 0x12, 0xA4, 0xA5) &&
               ModifierActive(HotkeyModifiers.Shift, 0x10, 0xA0, 0xA1) &&
               ModifierActive(HotkeyModifiers.Windows, 0x5B, 0x5C);
    }

    private bool ModifierActive(HotkeyModifiers modifier, params int[] virtualKeys)
    {
        if (!CurrentGesture.Modifiers.HasFlag(modifier))
        {
            return true;
        }

        return virtualKeys.Any(_pressedKeys.Contains);
    }
}
