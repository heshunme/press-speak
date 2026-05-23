using HsAsrDictation.Hotkeys;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class HotkeyCaptureParserTests
{
    private static readonly HotkeyPhysicalKey RightAltKey = new(0xA5, 0x38, true);
    private static readonly HotkeyPhysicalKey LeftControlKey = new(0xA2, 0x1D, false);
    private static readonly HotkeyPhysicalKey KKey = new(0x4B, 0x25, false);
    private static readonly HotkeyPhysicalKey LeftWindowsKey = new(0x5B, 0x5B, true);
    private static readonly HotkeyPhysicalKey EscapeKey = new(0x1B, 0x01, false);

    [Fact]
    public void TryCreateGesture_Succeeds_ForSinglePhysicalKey()
    {
        var succeeded = HotkeyCaptureParser.TryCreateGesture(
            [RightAltKey],
            out var gesture,
            out var failureReason);

        Assert.True(succeeded);
        Assert.Equal(HotkeyCaptureFailureReason.None, failureReason);
        Assert.NotNull(gesture);
        Assert.True(gesture!.IsEquivalentTo(new HotkeyGesture
        {
            Keys = [RightAltKey]
        }));
    }

    [Fact]
    public void TryCreateGesture_Succeeds_ForCombinationAndNormalizesAltGrCompanionControl()
    {
        var succeeded = HotkeyCaptureParser.TryCreateGesture(
            [LeftControlKey, RightAltKey, KKey],
            out var gesture,
            out var failureReason);

        Assert.True(succeeded);
        Assert.Equal(HotkeyCaptureFailureReason.None, failureReason);
        Assert.NotNull(gesture);
        Assert.True(gesture!.IsEquivalentTo(new HotkeyGesture
        {
            Keys = [RightAltKey, KKey]
        }));
    }

    [Fact]
    public void TryCreateGesture_Fails_WhenNoKeysAreCaptured()
    {
        var succeeded = HotkeyCaptureParser.TryCreateGesture(
            [],
            out var gesture,
            out var failureReason);

        Assert.False(succeeded);
        Assert.Null(gesture);
        Assert.Equal(HotkeyCaptureFailureReason.NoKeys, failureReason);
    }

    [Fact]
    public void TryCreateGesture_Fails_ForSingleWindowsKey()
    {
        var succeeded = HotkeyCaptureParser.TryCreateGesture(
            [LeftWindowsKey],
            out var gesture,
            out var failureReason);

        Assert.False(succeeded);
        Assert.Null(gesture);
        Assert.Equal(HotkeyCaptureFailureReason.SingleWindowsKeyNotAllowed, failureReason);
    }

    [Fact]
    public void TryCreateGesture_Fails_ForEscape()
    {
        var succeeded = HotkeyCaptureParser.TryCreateGesture(
            [EscapeKey],
            out var gesture,
            out var failureReason);

        Assert.False(succeeded);
        Assert.Null(gesture);
        Assert.Equal(HotkeyCaptureFailureReason.EscapeReserved, failureReason);
    }

    [Fact]
    public void TryCreateGesture_AllowsWindowsKeyInCombination()
    {
        var succeeded = HotkeyCaptureParser.TryCreateGesture(
            [LeftWindowsKey, KKey],
            out var gesture,
            out var failureReason);

        Assert.True(succeeded);
        Assert.Equal(HotkeyCaptureFailureReason.None, failureReason);
        Assert.NotNull(gesture);
        Assert.True(gesture!.IsEquivalentTo(new HotkeyGesture
        {
            Keys = [LeftWindowsKey, KKey]
        }));
    }
}
