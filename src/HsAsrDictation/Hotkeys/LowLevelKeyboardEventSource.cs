using System.Diagnostics;
using System.Runtime.InteropServices;
using HsAsrDictation.Interop;
using HsAsrDictation.Logging;

namespace HsAsrDictation.Hotkeys;

public sealed class LowLevelKeyboardEventSource : IDisposable
{
    private readonly LocalLogService _logger;
    private readonly Win32.LowLevelKeyboardProc _hookCallback;
    private readonly QueuedEventDispatcher<HotkeyEventData> _dispatcher;
    private IntPtr _hookHandle = IntPtr.Zero;

    public LowLevelKeyboardEventSource(LocalLogService logger)
    {
        _logger = logger;
        _hookCallback = HookCallback;
        // KeyEvent 订阅链上有日志 IO、录音启动等重活，必须离开钩子回调执行；
        // 无界队列 + 单消费者保证钩子回调立即返回，且事件按原始顺序逐个派发。
        _dispatcher = new QueuedEventDispatcher<HotkeyEventData>(RaiseKeyEvent, logger);
    }

    /// <summary>在后台派发线程上按事件原始顺序触发；订阅方不得在回调里做跨线程 UI 访问。</summary>
    public event EventHandler<HotkeyEventData>? KeyEvent;

    /// <summary>吞键判断，在低级键盘钩子回调里同步执行，必须保持轻量。</summary>
    public Func<HotkeyEventData, bool>? ShouldSuppressKeyEvent { get; set; }

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
        if (_hookHandle != IntPtr.Zero)
        {
            if (!Win32.UnhookWindowsHookEx(_hookHandle))
            {
                _logger.Warn("注销全局键盘钩子失败。");
            }

            _hookHandle = IntPtr.Zero;
        }

        // 不再产生新事件后，等队列里剩余事件按序处理完再返回。
        _dispatcher.Dispose();
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
                // 吞键决策必须同步完成；事件处理入队后在后台消费者上按序执行，
                // 避免重活超过 LowLevelHooksTimeout 导致 Windows 静默摘钩子。
                var suppress = ShouldSuppressKeyEvent?.Invoke(keyEvent) == true;
                _dispatcher.Enqueue(keyEvent);
                if (suppress)
                {
                    return (IntPtr)1;
                }
            }
        }

        return Win32.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void RaiseKeyEvent(HotkeyEventData keyEvent) => KeyEvent?.Invoke(this, keyEvent);
}
