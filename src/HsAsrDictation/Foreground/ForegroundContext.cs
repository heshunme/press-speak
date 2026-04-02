namespace HsAsrDictation.Foreground;

public sealed class ForegroundContext
{
    public IntPtr WindowHandle { get; init; }

    public string WindowTitle { get; init; } = string.Empty;

    public string ClassName { get; init; } = string.Empty;

    public bool IsPasswordField { get; init; }
}
