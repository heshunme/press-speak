namespace HsAsrDictation.Asr;

public interface IStreamingAsrSession : IDisposable
{
    ValueTask AcceptAudioAsync(float[] pcm16kMonoChunk, CancellationToken ct = default);

    StreamingAsrResult GetCurrentResult();

    ValueTask<StreamingAsrResult> CompleteAsync(CancellationToken ct = default);
}
