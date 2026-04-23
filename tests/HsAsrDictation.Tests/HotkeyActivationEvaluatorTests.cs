using HsAsrDictation.Hotkeys;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class HotkeyActivationEvaluatorTests
{
    private static readonly HotkeyBindingSnapshot AltOem3Binding = new(
        HotkeyModifiers.Alt,
        0xC0,
        0x29,
        false);

    [Fact]
    public void IsActive_ReturnsFalse_WhenOnlyPrimaryKeyIsPressed()
    {
        var state = new HotkeyPressedState();
        var primaryDown = new HotkeyEventData(0xC0, 0x29, false, true, false);

        state.Apply(primaryDown);

        Assert.False(HotkeyActivationEvaluator.IsActive(AltOem3Binding, state, primaryDown));
    }

    [Fact]
    public void IsActive_ReturnsTrue_WhenAltAndPrimaryArePressed()
    {
        var state = new HotkeyPressedState();
        state.Apply(new HotkeyEventData(0xA4, 0x38, false, true, false));
        var primaryDown = new HotkeyEventData(0xC0, 0x29, false, true, true);

        state.Apply(primaryDown);

        Assert.True(HotkeyActivationEvaluator.IsActive(AltOem3Binding, state, primaryDown));
    }

    [Fact]
    public void IsActive_ReturnsFalse_AfterPrimaryKeyIsReleased()
    {
        var state = new HotkeyPressedState();
        state.Apply(new HotkeyEventData(0xA4, 0x38, false, true, false));
        state.Apply(new HotkeyEventData(0xC0, 0x29, false, true, true));
        var primaryUp = new HotkeyEventData(0xC0, 0x29, false, false, true);

        state.Apply(primaryUp);

        Assert.False(HotkeyActivationEvaluator.IsActive(AltOem3Binding, state, primaryUp));
    }

    [Fact]
    public void IsActive_ReturnsFalse_AfterModifierIsReleased()
    {
        var state = new HotkeyPressedState();
        state.Apply(new HotkeyEventData(0xA4, 0x38, false, true, false));
        state.Apply(new HotkeyEventData(0xC0, 0x29, false, true, true));
        var altUp = new HotkeyEventData(0xA4, 0x38, false, false, false);

        state.Apply(altUp);

        Assert.False(HotkeyActivationEvaluator.IsActive(AltOem3Binding, state, altUp));
    }

    [Fact]
    public void IsActive_Treats_RightAltAsAltModifier()
    {
        var state = new HotkeyPressedState();
        state.Apply(new HotkeyEventData(0xA5, 0x38, true, true, false));
        var primaryDown = new HotkeyEventData(0xC0, 0x29, false, true, true);

        state.Apply(primaryDown);

        Assert.True(HotkeyActivationEvaluator.IsActive(AltOem3Binding, state, primaryDown));
    }

    [Fact]
    public void IsActive_FallsBackToVirtualKey_WhenScanCodeDiffers()
    {
        var state = new HotkeyPressedState();
        state.Apply(new HotkeyEventData(0xA4, 0x38, false, true, false));
        var primaryDown = new HotkeyEventData(0xC0, 0x70, false, true, true);

        state.Apply(primaryDown);

        Assert.True(HotkeyActivationEvaluator.IsActive(AltOem3Binding, state, primaryDown));
    }

    [Fact]
    public void IsActive_ReturnsFalse_WhenVirtualKeyFallbackKeyIsReleased()
    {
        var state = new HotkeyPressedState();
        state.Apply(new HotkeyEventData(0xA4, 0x38, false, true, false));
        state.Apply(new HotkeyEventData(0xC0, 0x70, false, true, true));
        var primaryUp = new HotkeyEventData(0xC0, 0x70, false, false, true);

        state.Apply(primaryUp);

        Assert.False(HotkeyActivationEvaluator.IsActive(AltOem3Binding, state, primaryUp));
    }

    [Fact]
    public void IsActive_MatchesExtendedKeyBinding()
    {
        var binding = new HotkeyBindingSnapshot(
            HotkeyModifiers.Control | HotkeyModifiers.Shift,
            0x2E,
            0x53,
            true);
        var state = new HotkeyPressedState();
        state.Apply(new HotkeyEventData(0xA2, 0x1D, false, true, false));
        state.Apply(new HotkeyEventData(0xA0, 0x2A, false, true, false));
        var deleteDown = new HotkeyEventData(0x2E, 0x53, true, true, false);

        state.Apply(deleteDown);

        Assert.True(HotkeyActivationEvaluator.IsActive(binding, state, deleteDown));
    }

    [Fact]
    public void SetPressedModifiers_InitializesPressedModifiers()
    {
        var state = new HotkeyPressedState();

        state.SetPressedModifiers(HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Windows);

        Assert.Equal(
            HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Windows,
            state.GetPressedModifiers(includeAltContext: false));
    }

    [Fact]
    public void SetPressedModifiers_AllowsModifierReleaseToClearState()
    {
        var state = new HotkeyPressedState();
        state.SetPressedModifiers(HotkeyModifiers.Control);

        state.Apply(new HotkeyEventData(0xA2, 0x1D, false, false, false));

        Assert.Equal(HotkeyModifiers.None, state.GetPressedModifiers(includeAltContext: false));
    }
}
