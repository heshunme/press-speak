using HsAsrDictation.Hotkeys;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class HotkeyDisplayPresentationTests
{
    [Fact]
    public void BuildDisplayText_ReturnsCurrentHotkey_WhenNoPendingHotkey()
    {
        var text = HotkeyDisplayPresentation.BuildDisplayText("Ctrl + Alt + Space", "Ctrl + Alt + Space", hasPendingHotkey: false);

        Assert.Equal("当前热键：Ctrl + Alt + Space", text);
    }

    [Fact]
    public void BuildDisplayText_ReturnsPendingHotkey_WhenPendingHotkeyExists()
    {
        var text = HotkeyDisplayPresentation.BuildDisplayText("Ctrl + Alt + Space", "Ctrl + Shift + D", hasPendingHotkey: true);

        Assert.Equal("待保存热键：Ctrl + Shift + D", text);
    }

    [Fact]
    public void BuildIdlePrompt_ShowsCurrentRuntimeHotkey()
    {
        var prompt = HotkeyDisplayPresentation.BuildIdlePrompt("Ctrl + Alt + Space");

        Assert.Equal("当前启用热键：Ctrl + Alt + Space。点击“开始录入”，然后按下要作为热键的组合键。", prompt);
    }

    [Fact]
    public void BuildCapturedPrompt_ShowsCurrentAndPendingHotkeys()
    {
        var prompt = HotkeyDisplayPresentation.BuildCapturedPrompt("Ctrl + Alt + Space", "Ctrl + Shift + D");

        Assert.Equal("已记录新热键。当前启用热键：Ctrl + Alt + Space。待保存热键：Ctrl + Shift + D，点击保存后生效。", prompt);
    }

    [Fact]
    public void BuildCanceledPrompt_RetainsPendingHotkey_WhenCaptureIsCanceledWithPendingChange()
    {
        var prompt = HotkeyDisplayPresentation.BuildCanceledPrompt("Ctrl + Alt + Space", "Ctrl + Shift + D", hasPendingHotkey: true);

        Assert.Equal("已取消本次录入。当前启用热键：Ctrl + Alt + Space。待保存热键：Ctrl + Shift + D，点击保存后生效。", prompt);
    }

    [Fact]
    public void BuildCanceledPrompt_ShowsCurrentHotkey_WhenNoPendingChangeRemains()
    {
        var prompt = HotkeyDisplayPresentation.BuildCanceledPrompt("Ctrl + Alt + Space", "Ctrl + Alt + Space", hasPendingHotkey: false);

        Assert.Equal("已取消热键录入。当前启用热键：Ctrl + Alt + Space。", prompt);
    }
}
