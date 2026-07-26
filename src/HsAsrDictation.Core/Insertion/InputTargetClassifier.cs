using HsAsrDictation.Foreground;

namespace HsAsrDictation.Insertion;

internal static class InputTargetClassifier
{
    private static readonly HashSet<string> TerminalProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal",
        "OpenConsole",
        "conhost",
        "powershell",
        "pwsh"
    };

    private static readonly HashSet<string> TerminalWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ConsoleWindowClass",
        "CASCADIA_HOSTING_WINDOW_CLASS"
    };

    private static readonly HashSet<string> EditorHostProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Code",
        "Code - Insiders",
        "Cursor",
        "Windsurf",
        "VSCodium"
    };

    private static readonly string[] TerminalKeywords =
    [
        "terminal",
        "console",
        "powershell",
        "pwsh",
        "cmd",
        "bash",
        "zsh",
        "fish",
        "shell",
        "xterm",
        "pty"
    ];

    public static bool ShouldPreferClipboardInsertion(ForegroundContext context) => IsTerminalLike(context);

    public static bool ShouldSkipUiAutomationFocusRestore(ForegroundContext context) => IsTerminalLike(context);

    private static bool IsTerminalLike(ForegroundContext context)
    {
        if (TerminalProcessNames.Contains(context.ProcessName) ||
            TerminalWindowClasses.Contains(context.ClassName))
        {
            return true;
        }

        if (EditorHostProcessNames.Contains(context.ProcessName))
        {
            return HasTerminalHint(context.WindowTitle) ||
                   HasTerminalHint(context.FocusedElementName) ||
                   HasTerminalHint(context.FocusedElementClassName) ||
                   HasTerminalHint(context.FocusedControlType);
        }

        return HasTerminalHint(context.WindowTitle) &&
               (HasTerminalHint(context.ProcessName) || HasTerminalHint(context.ClassName));
    }

    private static bool HasTerminalHint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return TerminalKeywords.Any(keyword =>
            value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
