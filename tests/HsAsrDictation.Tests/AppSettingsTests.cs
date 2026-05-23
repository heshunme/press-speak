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

    [Fact]
    public void Normalize_ClampsMaxRecordingDurationSeconds_ToSupportedRange()
    {
        var tooSmall = new AppSettings
        {
            MaxRecordingDurationSeconds = AppSettings.MinMaxRecordingDurationSeconds - 1
        };
        var tooLarge = new AppSettings
        {
            MaxRecordingDurationSeconds = AppSettings.MaxMaxRecordingDurationSeconds + 1
        };

        var normalizedTooSmall = tooSmall.Normalize();
        var normalizedTooLarge = tooLarge.Normalize();

        Assert.Equal(AppSettings.MinMaxRecordingDurationSeconds, normalizedTooSmall.MaxRecordingDurationSeconds);
        Assert.Equal(AppSettings.MaxMaxRecordingDurationSeconds, normalizedTooLarge.MaxRecordingDurationSeconds);
    }

    [Fact]
    public void Normalize_ClampsHotkeyReleaseTailDurationMilliseconds_ToSupportedRange()
    {
        var tooSmall = new AppSettings
        {
            HotkeyReleaseTailDurationMilliseconds = AppSettings.MinHotkeyReleaseTailDurationMilliseconds - 1
        };
        var tooLarge = new AppSettings
        {
            HotkeyReleaseTailDurationMilliseconds = AppSettings.MaxHotkeyReleaseTailDurationMilliseconds + 1
        };

        var normalizedTooSmall = tooSmall.Normalize();
        var normalizedTooLarge = tooLarge.Normalize();

        Assert.Equal(
            AppSettings.MinHotkeyReleaseTailDurationMilliseconds,
            normalizedTooSmall.HotkeyReleaseTailDurationMilliseconds);
        Assert.Equal(
            AppSettings.MaxHotkeyReleaseTailDurationMilliseconds,
            normalizedTooLarge.HotkeyReleaseTailDurationMilliseconds);
    }
}
