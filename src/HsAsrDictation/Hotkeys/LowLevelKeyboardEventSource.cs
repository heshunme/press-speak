using System.Diagnostics;
using System.Runtime.InteropServices;
using HsAsrDictation.Interop;
using HsAsrDictation.Logging;

namespace HsAsrDictation.Hotkeys;

public sealed class LowLevelKeyboardEventSource : IDisposable
{
    private readonly LocalLogService _logger;
    private readonly Win32.LowLevelKeyboardProc _hookCallback;
    private IntPtr _hookHandle = IntPtr.Zero;

    public LowLevelKeyboardEventSource(LocalLogService logger)
    {
        _logger = logger;
        _hookCallback = HookCallback;
    }

    public event EventHandler<HotkeyEventData>? KeyEvent;

    public void Start()
    {
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
    }

    public void Dispose()
    {
        if (_hookHandle == IntPtr.Zero)
        {
            return;
        }

        if (!Win32.UnhookWindowsHookEx(_hookHandle))
        {
            _logger.Warn("注销全局键盘钩子失败。");
        }

        _hookHandle = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            var message = wParam.ToInt32();
            var keyEvent = HotkeyEventTranslator.FromHook(hookStruct, message);
            if (!keyEvent.IsInjected)
            {
                KeyEvent?.Invoke(this, keyEvent);
            }
        }

        return Win32.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }
}
