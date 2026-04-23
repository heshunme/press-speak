using HsAsrDictation.Hotkeys;
using HsAsrDictation.Interop;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class HotkeyEventTranslatorTests
{
    [Fact]
    public void FromWindowMessage_ProducesSameEventDataAsHook_ForStandardKeyDown()
    {
        const int virtualKey = 0xC0;
        const int scanCode = 0x29;
        var lParam = CreateWindowMessageLParam(scanCode, isExtendedKey: false);
        var hookStruct = new Win32.KBDLLHOOKSTRUCT
        {
            vkCode = virtualKey,
            scanCode = scanCode,
            flags = 0
        };

        var fromWindow = HotkeyEventTranslator.FromWindowMessage(Win32.WM_KEYDOWN, (IntPtr)virtualKey, lParam);
        var fromHook = HotkeyEventTranslator.FromHook(hookStruct, Win32.WM_KEYDOWN);

        Assert.Equal(fromWindow, fromHook);
    }

    [Fact]
    public void FromWindowMessage_ProducesSameEventDataAsHook_ForAltContextKeyDown()
    {
        const int virtualKey = 0xC0;
        const int scanCode = 0x29;
        var lParam = CreateWindowMessageLParam(scanCode, isExtendedKey: false);
        var hookStruct = new Win32.KBDLLHOOKSTRUCT
        {
            vkCode = virtualKey,
            scanCode = scanCode,
            flags = Win32.LLKHF_ALTDOWN
        };

        var fromWindow = HotkeyEventTranslator.FromWindowMessage(Win32.WM_SYSKEYDOWN, (IntPtr)virtualKey, lParam);
        var fromHook = HotkeyEventTranslator.FromHook(hookStruct, Win32.WM_SYSKEYDOWN);

        Assert.Equal(fromWindow, fromHook);
        Assert.True(fromWindow.IsAltContext);
    }

    [Fact]
    public void FromWindowMessage_ProducesSameEventDataAsHook_ForExtendedKey()
    {
        const int virtualKey = 0x2E;
        const int scanCode = 0x53;
        var lParam = CreateWindowMessageLParam(scanCode, isExtendedKey: true);
        var hookStruct = new Win32.KBDLLHOOKSTRUCT
        {
            vkCode = virtualKey,
            scanCode = scanCode,
            flags = Win32.LLKHF_EXTENDED
        };

        var fromWindow = HotkeyEventTranslator.FromWindowMessage(Win32.WM_KEYDOWN, (IntPtr)virtualKey, lParam);
        var fromHook = HotkeyEventTranslator.FromHook(hookStruct, Win32.WM_KEYDOWN);

        Assert.Equal(fromWindow, fromHook);
        Assert.True(fromWindow.IsExtendedKey);
    }

    private static IntPtr CreateWindowMessageLParam(int scanCode, bool isExtendedKey)
    {
        long lParam = (long)(scanCode << 16);
        if (isExtendedKey)
        {
            lParam |= 1L << 24;
        }

        return (IntPtr)lParam;
    }
}
