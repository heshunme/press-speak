using HsAsrDictation.Hotkeys;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class HotkeyActivationEvaluatorTests
{
    private static readonly HotkeyPhysicalKey RightAltKey = new(0xA5, 0x38, true);
    private static readonly HotkeyPhysicalKey LeftAltKey = new(0xA4, 0x38, false);
    private static readonly HotkeyPhysicalKey LeftControlKey = new(0xA2, 0x1D, false);
    private static readonly HotkeyPhysicalKey KKey = new(0x4B, 0x25, false);

    [Fact]
    public void IsActive_ReturnsTrue_WhenPressedKeysExactlyMatchBinding()
    {
        var state = new HotkeyPressedState();
        state.Apply(CreateKeyEvent(RightAltKey, isKeyDown: true));
        var gesture = new HotkeyGesture
        {
            Keys = [RightAltKey]
        };

        Assert.True(HotkeyActivationEvaluator.IsActive(gesture, state.PressedKeys));
    }

    [Fact]
    public void IsActive_ReturnsFalse_WhenLeftAltIsPressedForRightAltBinding()
    {
        var state = new HotkeyPressedState();
        state.Apply(CreateKeyEvent(LeftAltKey, isKeyDown: true));
        var gesture = new HotkeyGesture
        {
            Keys = [RightAltKey]
        };

        Assert.False(HotkeyActivationEvaluator.IsActive(gesture, state.PressedKeys));
    }

    [Fact]
    public void IsActive_ReturnsFalse_WhenExtraPhysicalKeyIsPressed()
    {
        var state = new HotkeyPressedState();
        state.Apply(CreateKeyEvent(RightAltKey, isKeyDown: true));
        state.Apply(CreateKeyEvent(KKey, isKeyDown: true));
        var gesture = new HotkeyGesture
        {
            Keys = [RightAltKey]
        };

        Assert.False(HotkeyActivationEvaluator.IsActive(gesture, state.PressedKeys));
    }

    [Fact]
    public void IsActive_MatchesCombinationRegardlessOfPressOrder()
    {
        var state = new HotkeyPressedState();
        state.Apply(CreateKeyEvent(KKey, isKeyDown: true));
        state.Apply(CreateKeyEvent(RightAltKey, isKeyDown: true));
        var gesture = new HotkeyGesture
        {
            Keys = [RightAltKey, KKey]
        };

        Assert.True(HotkeyActivationEvaluator.IsActive(gesture, state.PressedKeys));
    }

    [Fact]
    public void IsActive_IgnoresAltGrCompanionLeftControl()
    {
        var state = new HotkeyPressedState();
        state.Apply(CreateKeyEvent(LeftControlKey, isKeyDown: true));
        state.Apply(CreateKeyEvent(RightAltKey, isKeyDown: true));
        var gesture = new HotkeyGesture
        {
            Keys = [RightAltKey]
        };

        Assert.True(HotkeyActivationEvaluator.IsActive(gesture, state.PressedKeys));
    }

    [Fact]
    public void IsActive_WithNormalizedKeys_ReturnsTrue_WhenPressedKeysExactlyMatchBinding()
    {
        var state = new HotkeyPressedState();
        state.Apply(CreateKeyEvent(RightAltKey, isKeyDown: true));
        var normalizedGestureKeys = new HotkeyGesture
        {
            Keys = [RightAltKey]
        }.Normalize().Keys;

        Assert.True(HotkeyActivationEvaluator.IsActive(normalizedGestureKeys, state.PressedKeySet));
    }

    [Fact]
    public void IsActive_WithNormalizedKeys_ReturnsFalse_WhenExtraPhysicalKeyIsPressed()
    {
        var state = new HotkeyPressedState();
        state.Apply(CreateKeyEvent(RightAltKey, isKeyDown: true));
        state.Apply(CreateKeyEvent(KKey, isKeyDown: true));
        var normalizedGestureKeys = new HotkeyGesture
        {
            Keys = [RightAltKey]
        }.Normalize().Keys;

        Assert.False(HotkeyActivationEvaluator.IsActive(normalizedGestureKeys, state.PressedKeySet));
    }

    [Fact]
    public void IsActive_WithNormalizedKeys_IgnoresAltGrCompanionLeftControl()
    {
        var state = new HotkeyPressedState();
        state.Apply(CreateKeyEvent(LeftControlKey, isKeyDown: true));
        state.Apply(CreateKeyEvent(RightAltKey, isKeyDown: true));
        var normalizedGestureKeys = new HotkeyGesture
        {
            Keys = [RightAltKey]
        }.Normalize().Keys;

        Assert.True(HotkeyActivationEvaluator.IsActive(normalizedGestureKeys, state.PressedKeySet));
    }

    [Fact]
    public void IsActive_WithNormalizedKeys_MatchesCombinationRegardlessOfPressOrder()
    {
        var state = new HotkeyPressedState();
        state.Apply(CreateKeyEvent(KKey, isKeyDown: true));
        state.Apply(CreateKeyEvent(RightAltKey, isKeyDown: true));
        var normalizedGestureKeys = new HotkeyGesture
        {
            Keys = [RightAltKey, KKey]
        }.Normalize().Keys;

        Assert.True(HotkeyActivationEvaluator.IsActive(normalizedGestureKeys, state.PressedKeySet));
    }

    [Fact]
    public void IsSubsetMatch_WithNormalizedKeys_ReturnsTrue_ForModifierPrefix()
    {
        var state = new HotkeyPressedState();
        state.Apply(CreateKeyEvent(LeftAltKey, isKeyDown: true));
        var normalizedGestureKeys = new HotkeyGesture
        {
            Keys = [LeftAltKey, KKey]
        }.Normalize().Keys;

        Assert.True(HotkeyActivationEvaluator.IsSubsetMatch(normalizedGestureKeys, state.PressedKeySet));
    }

    [Fact]
    public void IsSubsetMatch_WithNormalizedKeys_ReturnsFalse_WhenPressedKeyOutsideGesture()
    {
        var state = new HotkeyPressedState();
        state.Apply(CreateKeyEvent(LeftAltKey, isKeyDown: true));
        state.Apply(CreateKeyEvent(RightAltKey, isKeyDown: true));
        var normalizedGestureKeys = new HotkeyGesture
        {
            Keys = [LeftAltKey, KKey]
        }.Normalize().Keys;

        Assert.False(HotkeyActivationEvaluator.IsSubsetMatch(normalizedGestureKeys, state.PressedKeySet));
    }

    [Fact]
    public void IsSubsetMatch_WithNormalizedKeys_ReturnsFalse_WhenNoKeysPressed()
    {
        var state = new HotkeyPressedState();
        var normalizedGestureKeys = new HotkeyGesture
        {
            Keys = [LeftAltKey, KKey]
        }.Normalize().Keys;

        Assert.False(HotkeyActivationEvaluator.IsSubsetMatch(normalizedGestureKeys, state.PressedKeySet));
    }

    private static HotkeyEventData CreateKeyEvent(HotkeyPhysicalKey key, bool isKeyDown) =>
        new(key.VirtualKey, key.ScanCode, key.IsExtendedKey, isKeyDown, false, false);
}
