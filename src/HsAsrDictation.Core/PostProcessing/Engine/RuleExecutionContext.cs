namespace HsAsrDictation.PostProcessing.Engine;

public sealed class RuleExecutionContext
{
    public string? ProcessName { get; init; }

    public string? WindowTitle { get; init; }

    public bool IsPasswordField { get; init; }
}
