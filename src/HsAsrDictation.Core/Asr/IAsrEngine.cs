namespace HsAsrDictation.Asr;

public interface IAsrEngine : IDisposable
{
    bool IsReady { get; }

    /// <summary>
    /// 确保引擎已按当前设置初始化。已就绪且设置指纹未变时跳过重复 provisioning；
    /// <paramref name="forceReprovision"/> 为 true（设置保存重建、重新下载）时强制走完整 provisioning。
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default, bool forceReprovision = false);

    /// <summary>异步卸载：等待进行中的解码结束后再释放识别器，不阻塞调用线程。</summary>
    Task UnloadAsync();

    Task<AsrResult> TranscribeAsync(float[] pcm16kMono, CancellationToken ct = default);
}
