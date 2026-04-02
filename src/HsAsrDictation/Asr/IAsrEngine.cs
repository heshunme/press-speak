namespace HsAsrDictation.Asr;

public interface IAsrEngine : IDisposable
{
    bool IsReady { get; }

    Task InitializeAsync(CancellationToken ct = default);

    Task<AsrResult> TranscribeAsync(float[] pcm16kMono, CancellationToken ct = default);
}
