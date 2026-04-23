namespace HsAsrDictation.Hotkeys;

public sealed class HotkeyPressedState
{
    private readonly HashSet<HotkeyPhysicalKey> _pressedPhysicalKeys = [];
    private bool _leftControlPressed;
    private bool _rightControlPressed;
    private bool _leftAltPressed;
    private bool _rightAltPressed;
    private bool _leftShiftPressed;
    private bool _rightShiftPressed;
    private bool _leftWindowsPressed;
    private bool _rightWindowsPressed;

    public void Clear() => ResetModifiersAndKeys();

    public void SetPressedModifiers(HotkeyModifiers modifiers)
    {
        _pressedPhysicalKeys.Clear();
        _leftControlPressed = modifiers.HasFlag(HotkeyModifiers.Control);
        _rightControlPressed = false;
        _leftAltPressed = modifiers.HasFlag(HotkeyModifiers.Alt);
        _rightAltPressed = false;
        _leftShiftPressed = modifiers.HasFlag(HotkeyModifiers.Shift);
        _rightShiftPressed = false;
        _leftWindowsPressed = modifiers.HasFlag(HotkeyModifiers.Windows);
        _rightWindowsPressed = false;
    }

    public void Apply(HotkeyEventData keyEvent)
    {
        UpdateModifierState(keyEvent);

        if (keyEvent.ScanCode <= 0 || keyEvent.IsModifier)
        {
            return;
        }

        var key = new HotkeyPhysicalKey(keyEvent.ScanCode, keyEvent.IsExtendedKey);
        if (keyEvent.IsKeyDown)
        {
            _pressedPhysicalKeys.Add(key);
        }
        else
        {
            _pressedPhysicalKeys.Remove(key);
        }
    }

    public bool IsModifierPressed(HotkeyModifiers modifier, bool includeAltContext)
    {
        return modifier switch
        {
            HotkeyModifiers.Control => _leftControlPressed || _rightControlPressed,
            HotkeyModifiers.Alt => _leftAltPressed || _rightAltPressed || includeAltContext,
            HotkeyModifiers.Shift => _leftShiftPressed || _rightShiftPressed,
            HotkeyModifiers.Windows => _leftWindowsPressed || _rightWindowsPressed,
            _ => false
        };
    }

    public HotkeyModifiers GetPressedModifiers(bool includeAltContext)
    {
        var modifiers = HotkeyModifiers.None;
        if (IsModifierPressed(HotkeyModifiers.Control, includeAltContext: false))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (IsModifierPressed(HotkeyModifiers.Alt, includeAltContext))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsModifierPressed(HotkeyModifiers.Shift, includeAltContext: false))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (IsModifierPressed(HotkeyModifiers.Windows, includeAltContext: false))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        return modifiers;
    }

    public bool IsPhysicalKeyPressed(int scanCode, bool isExtendedKey) =>
        scanCode > 0 && _pressedPhysicalKeys.Contains(new HotkeyPhysicalKey(scanCode, isExtendedKey));

    private void ResetModifiersAndKeys()
    {
        _pressedPhysicalKeys.Clear();
        _leftControlPressed = false;
        _rightControlPressed = false;
        _leftAltPressed = false;
        _rightAltPressed = false;
        _leftShiftPressed = false;
        _rightShiftPressed = false;
        _leftWindowsPressed = false;
        _rightWindowsPressed = false;
    }

    private void UpdateModifierState(HotkeyEventData keyEvent)
    {
        switch (keyEvent.VirtualKey)
        {
            case 0x11:
            case 0xA2:
                _leftControlPressed = keyEvent.IsKeyDown;
                break;
            case 0xA3:
                _rightControlPressed = keyEvent.IsKeyDown;
                break;
            case 0x12:
            case 0xA4:
                _leftAltPressed = keyEvent.IsKeyDown;
                break;
            case 0xA5:
                _rightAltPressed = keyEvent.IsKeyDown;
                break;
            case 0x10:
            case 0xA0:
                _leftShiftPressed = keyEvent.IsKeyDown;
                break;
            case 0xA1:
                _rightShiftPressed = keyEvent.IsKeyDown;
                break;
            case 0x5B:
                _leftWindowsPressed = keyEvent.IsKeyDown;
                break;
            case 0x5C:
                _rightWindowsPressed = keyEvent.IsKeyDown;
                break;
        }
    }
}
