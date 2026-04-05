namespace HsAsrDictation.PostProcessing.Engine;

public sealed class PostProcessingTraceEntry
{
    public string RuleId { get; init; } = string.Empty;

    public string RuleName { get; init; } = string.Empty;

    public bool Changed { get; init; }

    public bool Failed { get; init; }

    public string Message { get; init; } = string.Empty;
}
