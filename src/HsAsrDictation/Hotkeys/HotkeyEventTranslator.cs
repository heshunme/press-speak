using HsAsrDictation.Interop;

namespace HsAsrDictation.Hotkeys;

internal static class HotkeyEventTranslator
{
    public static HotkeyEventData FromHook(Win32.KBDLLHOOKSTRUCT hookStruct, int message)
    {
        return new HotkeyEventData(
            unchecked((int)hookStruct.vkCode),
            unchecked((int)hookStruct.scanCode),
            (hookStruct.flags & Win32.LLKHF_EXTENDED) != 0,
            IsKeyDownMessage(message),
            (hookStruct.flags & Win32.LLKHF_ALTDOWN) != 0);
    }

    public static HotkeyEventData FromWindowMessage(int message, IntPtr wParam, IntPtr lParam)
    {
        var lParamValue = lParam.ToInt64();
        return new HotkeyEventData(
            wParam.ToInt32(),
            unchecked((int)((lParamValue >> 16) & 0xFF)),
            ((lParamValue >> 24) & 0x01) != 0,
            IsKeyDownMessage(message),
            message is Win32.WM_SYSKEYDOWN or Win32.WM_SYSKEYUP);
    }

    private static bool IsKeyDownMessage(int message) =>
        message is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN;
}
