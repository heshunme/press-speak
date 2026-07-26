namespace HsAsrDictation.Asr;

public sealed class StreamingAsrResult
{
    public string Text { get; init; } = string.Empty;

    public string StableText { get; init; } = string.Empty;

    public string PartialText { get; init; } = string.Empty;

    public bool IsFinal { get; init; }

    public TimeSpan AudioDuration { get; init; }

    public TimeSpan DecodeLatency { get; init; }

    public string? Error { get; init; }
}
