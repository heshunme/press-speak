namespace HsAsrDictation.Asr;

public interface IPunctuationService : IDisposable
{
    bool IsEnabled { get; }

    bool IsReady { get; }

    void Reload(PunctuationRuntimeOptions options);

    string TryAddPunctuation(string text);
}
