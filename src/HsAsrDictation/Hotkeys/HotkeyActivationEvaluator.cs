namespace HsAsrDictation.Hotkeys;

public static class HotkeyActivationEvaluator
{
    public static bool IsActive(HotkeyBindingSnapshot binding, HotkeyPressedState pressedState, HotkeyEventData currentEvent)
    {
        if (!PrimaryKeyMatches(binding, pressedState, currentEvent))
        {
            return false;
        }

        return ModifierMatches(binding.Modifiers, HotkeyModifiers.Control, pressedState, currentEvent) &&
               ModifierMatches(binding.Modifiers, HotkeyModifiers.Alt, pressedState, currentEvent) &&
               ModifierMatches(binding.Modifiers, HotkeyModifiers.Shift, pressedState, currentEvent) &&
               ModifierMatches(binding.Modifiers, HotkeyModifiers.Windows, pressedState, currentEvent);
    }

    private static bool PrimaryKeyMatches(HotkeyBindingSnapshot binding, HotkeyPressedState pressedState, HotkeyEventData currentEvent)
    {
        if (binding.PrimaryScanCode > 0)
        {
            return pressedState.IsPhysicalKeyPressed(binding.PrimaryScanCode, binding.IsExtendedKey) ||
                   (currentEvent.IsKeyDown &&
                    !currentEvent.IsModifier &&
                    currentEvent.ScanCode == binding.PrimaryScanCode &&
                    currentEvent.IsExtendedKey == binding.IsExtendedKey);
        }

        return currentEvent.IsKeyDown && currentEvent.VirtualKey == binding.VirtualKey;
    }

    private static bool ModifierMatches(
        HotkeyModifiers requiredModifiers,
        HotkeyModifiers modifier,
        HotkeyPressedState pressedState,
        HotkeyEventData currentEvent)
    {
        if (!requiredModifiers.HasFlag(modifier))
        {
            return true;
        }

        return pressedState.IsModifierPressed(modifier, includeAltContext: modifier == HotkeyModifiers.Alt && currentEvent.IsAltContext);
    }
}
