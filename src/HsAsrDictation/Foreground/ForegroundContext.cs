using System.Windows.Automation;

namespace HsAsrDictation.Foreground;

public sealed class ForegroundContext
{
    public IntPtr WindowHandle { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public string WindowTitle { get; init; } = string.Empty;

    public string ClassName { get; init; } = string.Empty;

    public bool IsPasswordField { get; init; }

    public AutomationElement? FocusedElement { get; init; }
}
