namespace HsAsrDictation.PostProcessing.Engine;

public sealed class PostProcessingTraceResult
{
    public string Input { get; init; } = string.Empty;

    public string Output { get; init; } = string.Empty;

    public bool UsedFallback { get; init; }

    public IReadOnlyList<PostProcessingTraceEntry> TraceEntries { get; init; } = [];
}
