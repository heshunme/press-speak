using HsAsrDictation.Hotkeys;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class HotkeySuppressionEvaluatorTests
{
    private static readonly HotkeyPhysicalKey LeftAltKey = new(0xA4, 0x38, false);
    private static readonly HotkeyPhysicalKey Oem3Key = new(0xC0, 0x29, false);
    private static readonly HotkeyPhysicalKey KKey = new(0x4B, 0x25, false);

    [Fact]
    public void ShouldSuppress_ReturnsTrue_ForSingleKeyGesture()
    {
        var gesture = new HotkeyGesture
        {
            Keys = [Oem3Key]
        };

        var result = HotkeySuppressionEvaluator.ShouldSuppress(
            gesture,
            [Oem3Key],
            CreateKeyEvent(Oem3Key, isKeyDown: true),
            wasGestureActive: false,
            isGestureActive: true);

        Assert.True(result);
    }

    [Fact]
    public void ShouldSuppress_ReturnsTrue_ForPrimaryKeyDown_WhenChordActivates()
    {
        var gesture = new HotkeyGesture
        {
            Keys = [LeftAltKey, Oem3Key]
        };

        var result = HotkeySuppressionEvaluator.ShouldSuppress(
            gesture,
            [LeftAltKey, Oem3Key],
            CreateKeyEvent(Oem3Key, isKeyDown: true),
            wasGestureActive: false,
            isGestureActive: true);

        Assert.True(result);
    }

    [Fact]
    public void ShouldSuppress_ReturnsTrue_ForPrimaryKeyUp_AfterChordWasActive()
    {
        var gesture = new HotkeyGesture
        {
            Keys = [LeftAltKey, Oem3Key]
        };

        var result = HotkeySuppressionEvaluator.ShouldSuppress(
            gesture,
            [LeftAltKey],
            CreateKeyEvent(Oem3Key, isKeyDown: false),
            wasGestureActive: true,
            isGestureActive: false);

        Assert.True(result);
    }

    [Fact]
    public void ShouldSuppress_ReturnsFalse_ForModifierOnlyPartOfChord()
    {
        var gesture = new HotkeyGesture
        {
            Keys = [LeftAltKey, Oem3Key]
        };

        var result = HotkeySuppressionEvaluator.ShouldSuppress(
            gesture,
            [LeftAltKey],
            CreateKeyEvent(LeftAltKey, isKeyDown: true),
            wasGestureActive: false,
            isGestureActive: false);

        Assert.False(result);
    }

    [Fact]
    public void ShouldSuppress_ReturnsFalse_ForUnrelatedKey()
    {
        var gesture = new HotkeyGesture
        {
            Keys = [LeftAltKey, Oem3Key]
        };

        var result = HotkeySuppressionEvaluator.ShouldSuppress(
            gesture,
            [LeftAltKey, KKey],
            CreateKeyEvent(KKey, isKeyDown: true),
            wasGestureActive: false,
            isGestureActive: false);

        Assert.False(result);
    }

    private static HotkeyEventData CreateKeyEvent(HotkeyPhysicalKey key, bool isKeyDown) =>
        new(key.VirtualKey, key.ScanCode, key.IsExtendedKey, isKeyDown, false, false);
}
