namespace HsAsrDictation.Asr;

public sealed class AsrResult
{
    public string Text { get; init; } = string.Empty;

    public TimeSpan AudioDuration { get; init; }

    public TimeSpan DecodeLatency { get; init; }

    public bool Success { get; init; }

    public string? Error { get; init; }
}
