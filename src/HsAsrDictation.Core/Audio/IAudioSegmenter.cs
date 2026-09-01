namespace HsAsrDictation.Audio;

/// <summary>
/// 把连续音频流按语音停顿切成段。实现不要求线程安全：调用方（分段解码流水线）
/// 保证所有成员都在同一个循环任务里串行调用。
/// </summary>
public interface IAudioSegmenter : IDisposable
{
    /// <summary>当前是否正在说话；用于松键时判断能否跳过尾录。</summary>
    bool IsSpeechActive { get; }

    /// <summary>喂入一块音频；长度任意，不要求对齐 VAD 窗口。</summary>
    void AcceptChunk(float[] samples);

    /// <summary>取出一个已封口的段；没有则返回 false。</summary>
    bool TryDequeue(out AudioSegment segment);

    /// <summary>
    /// 强制把当前未封口的段封口吐出（松键收尾、或单段超过硬上限时调用）。
    /// 调用后可继续 <see cref="AcceptChunk"/>，段偏移继续累加。
    /// </summary>
    void Flush();
}
