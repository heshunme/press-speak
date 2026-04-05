using HsAsrDictation.Hotkeys;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class HotkeyCaptureParserTests
{
    [Theory]
    [InlineData(0x20, HotkeyModifiers.Control | HotkeyModifiers.Alt, HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x20)]
    [InlineData(0xC0, HotkeyModifiers.Control, HotkeyModifiers.Control, 0xC0)]
    [InlineData(0x41, HotkeyModifiers.Control | HotkeyModifiers.Shift, HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x41)]
    [InlineData(0x77, HotkeyModifiers.Alt, HotkeyModifiers.Alt, 0x77)]
    public void TryCreateCandidate_ReturnsCandidate_ForSupportedCombination(
        int virtualKey,
        HotkeyModifiers modifiers,
        HotkeyModifiers expectedModifiers,
        int expectedVirtualKey)
    {
        var succeeded = HotkeyCaptureEvaluator.TryCreateCandidate(
            virtualKey,
            modifiers,
            out var candidate,
            out var failureReason);

        Assert.True(succeeded);
        Assert.Equal(HotkeyCaptureFailureReason.None, failureReason);
        Assert.Equal(expectedModifiers, candidate.Modifiers);
        Assert.Equal(expectedVirtualKey, candidate.VirtualKey);
    }

    [Theory]
    [InlineData(0xA2, HotkeyModifiers.Control)]
    [InlineData(0xA5, HotkeyModifiers.Alt)]
    [InlineData(0x5B, HotkeyModifiers.Windows)]
    public void TryCreateCandidate_Fails_WhenOnlyModifierKeyIsPressed(int virtualKey, HotkeyModifiers modifiers)
    {
        var succeeded = HotkeyCaptureEvaluator.TryCreateCandidate(
            virtualKey,
            modifiers,
            out var candidate,
            out var failureReason);

        Assert.False(succeeded);
        Assert.Equal(default, candidate);
        Assert.Equal(HotkeyCaptureFailureReason.MissingPrimaryKey, failureReason);
    }

    [Theory]
    [InlineData(0x41)]
    [InlineData(0x77)]
    [InlineData(0x20)]
    public void TryCreateCandidate_Fails_WhenModifierIsMissing(int virtualKey)
    {
        var succeeded = HotkeyCaptureEvaluator.TryCreateCandidate(
            virtualKey,
            HotkeyModifiers.None,
            out var candidate,
            out var failureReason);

        Assert.False(succeeded);
        Assert.Equal(default, candidate);
        Assert.Equal(HotkeyCaptureFailureReason.MissingModifier, failureReason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x1B)]
    public void TryCreateCandidate_Fails_WhenPrimaryKeyIsInvalid(int virtualKey)
    {
        var succeeded = HotkeyCaptureEvaluator.TryCreateCandidate(
            virtualKey,
            HotkeyModifiers.Control,
            out var candidate,
            out var failureReason);

        Assert.False(succeeded);
        Assert.Equal(default, candidate);
        Assert.NotEqual(HotkeyCaptureFailureReason.None, failureReason);
    }
}
