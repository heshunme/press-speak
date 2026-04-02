namespace HsAsrDictation.Models;

public sealed class ModelReadyResult
{
    public bool IsReady { get; init; }

    public string? ModelDirectory { get; init; }

    public string[] MissingEntries { get; init; } = [];

    public string? ErrorMessage { get; init; }
}
