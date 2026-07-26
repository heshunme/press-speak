namespace HsAsrDictation.Insertion;

public sealed class InsertionResult
{
    public bool Success { get; init; }

    public string Method { get; init; } = string.Empty;

    public string? Error { get; init; }
}
