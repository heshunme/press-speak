namespace HsAsrDictation.Hotkeys;

public static class HotkeyDisplayPresentation
{
    public static string BuildDisplayText(string currentHotkeyText, string candidateHotkeyText, bool hasPendingHotkey) =>
        hasPendingHotkey
            ? $"待保存热键：{candidateHotkeyText}"
            : $"当前热键：{currentHotkeyText}";

    public static string BuildIdlePrompt(string currentHotkeyText) =>
        $"当前启用热键：{currentHotkeyText}。点击“开始录入”，然后按下要作为热键的组合键。";

    public static string BuildPendingPrompt(string currentHotkeyText, string candidateHotkeyText) =>
        $"当前启用热键：{currentHotkeyText}。待保存热键：{candidateHotkeyText}，点击保存后生效。";

    public static string BuildCanceledPrompt(string currentHotkeyText, string candidateHotkeyText, bool hasPendingHotkey) =>
        hasPendingHotkey
            ? $"已取消本次录入。{BuildPendingPrompt(currentHotkeyText, candidateHotkeyText)}"
            : $"已取消热键录入。当前启用热键：{currentHotkeyText}。";

    public static string BuildCapturedPrompt(string currentHotkeyText, string candidateHotkeyText) =>
        $"已记录新热键。{BuildPendingPrompt(currentHotkeyText, candidateHotkeyText)}";
}
