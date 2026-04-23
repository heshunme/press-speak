using HsAsrDictation.Hotkeys;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class HotkeyCaptureParserTests
{
    [Theory]
    [InlineData(0x20, 0x39, false, HotkeyModifiers.Control | HotkeyModifiers.Alt, HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x20, 0x39, false)]
    [InlineData(0xC0, 0x29, false, HotkeyModifiers.Control, HotkeyModifiers.Control, 0xC0, 0x29, false)]
    [InlineData(0x41, 0x1E, false, HotkeyModifiers.Control | HotkeyModifiers.Shift, HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x41, 0x1E, false)]
    [InlineData(0x77, 0x41, false, HotkeyModifiers.Alt, HotkeyModifiers.Alt, 0x77, 0x41, false)]
    public void TryCreateCandidate_ReturnsCandidate_ForSupportedCombination(
        int virtualKey,
        int scanCode,
        bool isExtendedKey,
        HotkeyModifiers modifiers,
        HotkeyModifiers expectedModifiers,
        int expectedVirtualKey,
        int expectedScanCode,
        bool expectedIsExtendedKey)
    {
        var succeeded = HotkeyCaptureEvaluator.TryCreateCandidate(
            new HotkeyEventData(virtualKey, scanCode, isExtendedKey, true, false),
            modifiers,
            out var candidate,
            out var failureReason);

        Assert.True(succeeded);
        Assert.Equal(HotkeyCaptureFailureReason.None, failureReason);
        Assert.Equal(expectedModifiers, candidate.Modifiers);
        Assert.Equal(expectedVirtualKey, candidate.VirtualKey);
        Assert.Equal(expectedScanCode, candidate.ScanCode);
        Assert.Equal(expectedIsExtendedKey, candidate.IsExtendedKey);
    }

    [Theory]
    [InlineData(0xA2, 0x1D, false, HotkeyModifiers.Control)]
    [InlineData(0xA5, 0x38, true, HotkeyModifiers.Alt)]
    [InlineData(0x5B, 0x5B, true, HotkeyModifiers.Windows)]
    public void TryCreateCandidate_Fails_WhenOnlyModifierKeyIsPressed(
        int virtualKey,
        int scanCode,
        bool isExtendedKey,
        HotkeyModifiers modifiers)
    {
        var succeeded = HotkeyCaptureEvaluator.TryCreateCandidate(
            new HotkeyEventData(virtualKey, scanCode, isExtendedKey, true, false),
            modifiers,
            out var candidate,
            out var failureReason);

        Assert.False(succeeded);
        Assert.Equal(default, candidate);
        Assert.Equal(HotkeyCaptureFailureReason.MissingPrimaryKey, failureReason);
    }

    [Theory]
    [InlineData(0x41, 0x1E)]
    [InlineData(0x77, 0x41)]
    [InlineData(0x20, 0x39)]
    public void TryCreateCandidate_Fails_WhenModifierIsMissing(int virtualKey, int scanCode)
    {
        var succeeded = HotkeyCaptureEvaluator.TryCreateCandidate(
            new HotkeyEventData(virtualKey, scanCode, false, true, false),
            HotkeyModifiers.None,
            out var candidate,
            out var failureReason);

        Assert.False(succeeded);
        Assert.Equal(default, candidate);
        Assert.Equal(HotkeyCaptureFailureReason.MissingModifier, failureReason);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0x1B, 0x01)]
    public void TryCreateCandidate_Fails_WhenPrimaryKeyIsInvalid(int virtualKey, int scanCode)
    {
        var succeeded = HotkeyCaptureEvaluator.TryCreateCandidate(
            new HotkeyEventData(virtualKey, scanCode, false, true, false),
            HotkeyModifiers.Control,
            out var candidate,
            out var failureReason);

        Assert.False(succeeded);
        Assert.Equal(default, candidate);
        Assert.NotEqual(HotkeyCaptureFailureReason.None, failureReason);
    }

    [Fact]
    public void TryCreateCandidate_Succeeds_WhenModifierWasAlreadyHeldBeforePrimaryKey()
    {
        var state = new HotkeyPressedState();
        state.SetPressedModifiers(HotkeyModifiers.Control);
        var primaryDown = new HotkeyEventData(0xC0, 0x29, false, true, false);

        state.Apply(primaryDown);
        var succeeded = HotkeyCaptureEvaluator.TryCreateCandidate(
            primaryDown,
            state.GetPressedModifiers(includeAltContext: false),
            out var candidate,
            out var failureReason);

        Assert.True(succeeded);
        Assert.Equal(HotkeyCaptureFailureReason.None, failureReason);
        Assert.Equal(HotkeyModifiers.Control, candidate.Modifiers);
        Assert.Equal(0xC0, candidate.VirtualKey);
        Assert.Equal(0x29, candidate.ScanCode);
        Assert.False(candidate.IsExtendedKey);
    }
}
