using HsAsrDictation.Foreground;
using HsAsrDictation.Insertion;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class InputTargetClassifierTests
{
    [Fact]
    public void ShouldPreferClipboardInsertion_ReturnsTrue_ForWindowsTerminal()
    {
        var context = new ForegroundContext
        {
            ProcessName = "WindowsTerminal",
            ClassName = "CASCADIA_HOSTING_WINDOW_CLASS",
            WindowTitle = "PowerShell"
        };

        Assert.True(InputTargetClassifier.ShouldPreferClipboardInsertion(context));
        Assert.True(InputTargetClassifier.ShouldSkipUiAutomationFocusRestore(context));
    }

    [Fact]
    public void ShouldPreferClipboardInsertion_ReturnsTrue_ForVsCodeIntegratedTerminal()
    {
        var context = new ForegroundContext
        {
            ProcessName = "Code",
            ClassName = "Chrome_WidgetWin_1",
            WindowTitle = "demo - Visual Studio Code",
            FocusedElementName = "Terminal",
            FocusedElementClassName = "xterm",
            FocusedControlType = "ControlType.Document"
        };

        Assert.True(InputTargetClassifier.ShouldPreferClipboardInsertion(context));
    }

    [Fact]
    public void ShouldPreferClipboardInsertion_ReturnsFalse_ForNormalEditorWindow()
    {
        var context = new ForegroundContext
        {
            ProcessName = "notepad",
            ClassName = "Notepad",
            WindowTitle = "Untitled - Notepad",
            FocusedElementName = "Text Editor",
            FocusedElementClassName = "RichEditD2DPT",
            FocusedControlType = "ControlType.Document"
        };

        Assert.False(InputTargetClassifier.ShouldPreferClipboardInsertion(context));
        Assert.False(InputTargetClassifier.ShouldSkipUiAutomationFocusRestore(context));
    }
}
