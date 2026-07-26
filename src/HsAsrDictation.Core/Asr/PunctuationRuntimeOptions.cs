namespace HsAsrDictation.Asr;

public sealed class PunctuationRuntimeOptions
{
    public bool Enabled { get; init; }

    public string ModelPath { get; init; } = string.Empty;

    public int NumThreads { get; init; } = 1;
}
