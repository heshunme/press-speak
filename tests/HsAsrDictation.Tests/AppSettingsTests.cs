using HsAsrDictation.Hotkeys;
using HsAsrDictation.Settings;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void Normalize_FallsBackToDefaultHotkey_WhenHotkeyHasNoPhysicalKeys()
    {
        var settings = new AppSettings
        {
            Hotkey = new HotkeyGesture()
        };

        var normalized = settings.Normalize();

        Assert.True(normalized.Hotkey.IsEquivalentTo(HotkeyGesture.CreateDefault()));
    }

    [Fact]
    public void Normalize_FallsBackToDefaultHotkey_WhenHotkeyIsSingleWindowsKey()
    {
        var settings = new AppSettings
        {
            Hotkey = new HotkeyGesture
            {
                Keys = [new HotkeyPhysicalKey(0x5B, 0x5B, true)]
            }
        };

        var normalized = settings.Normalize();

        Assert.True(normalized.Hotkey.IsEquivalentTo(HotkeyGesture.CreateDefault()));
    }
}
