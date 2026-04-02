namespace HsAsrDictation.Asr;

public interface IAsrEngine : IDisposable
{
    bool IsReady { get; }

    Task InitializeAsync(CancellationToken ct = default);

    void Unload();

    Task<AsrResult> TranscribeAsync(float[] pcm16kMono, CancellationToken ct = default);
}
