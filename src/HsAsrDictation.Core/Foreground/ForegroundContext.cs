namespace HsAsrDictation.Foreground;

public sealed class ForegroundContext
{
    public IntPtr WindowHandle { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public string WindowTitle { get; init; } = string.Empty;

    public string ClassName { get; init; } = string.Empty;

    public bool IsPasswordField { get; init; }

    /// <summary>Windows 下为 System.Windows.Automation.AutomationElement；Core 保持无 UI 依赖，故用 object。</summary>
    public object? FocusedElement { get; init; }

    public string FocusedElementName { get; init; } = string.Empty;

    public string FocusedElementClassName { get; init; } = string.Empty;

    public string FocusedControlType { get; init; } = string.Empty;
}
