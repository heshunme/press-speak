namespace HsAsrDictation.PostProcessing.Engine;

public sealed class RuleApplyResult
{
    public string Output { get; init; } = string.Empty;

    public bool Changed { get; init; }

    public string? TraceMessage { get; init; }
}
