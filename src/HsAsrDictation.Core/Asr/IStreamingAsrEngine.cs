namespace HsAsrDictation.Asr;

public interface IStreamingAsrEngine : IDisposable
{
    bool IsReady { get; }

    Task InitializeAsync(CancellationToken ct = default);

    void Unload();

    IStreamingAsrSession CreateSession();
}
