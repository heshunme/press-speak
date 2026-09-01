namespace HsAsrDictation.Services;

/// <summary>
/// 分段解码的调参入口。默认值取"激进档"（见 docs/vad-segmented-decoding-design.md 第 5 节），
/// 目标是尽早出结果；真机量到 FunASR-Nano 的 RTF 后可能需要调大 <see cref="MinDecodeBatch"/>。
/// </summary>
public sealed class SegmentedDecodeOptions
{
    public static SegmentedDecodeOptions Default { get; } = new();

    /// <summary>判定"一句话说完"的静音时长。</summary>
    public TimeSpan MinSilenceDuration { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>短于此值的语音段被 VAD 丢弃。</summary>
    public TimeSpan MinSpeechDuration { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// 累计语音达到该长度才派发一批解码。太小会被 LLM 式解码的固定开销吃掉收益。
    /// </summary>
    public TimeSpan MinDecodeBatch { get; init; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>单段连续语音的硬上限，超过则强制封口，避免退化为整段解码。</summary>
    public TimeSpan MaxOpenSegment { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>段边界补齐量：VAD 边界紧贴语音起止，不补齐会削掉词头词尾。</summary>
    public TimeSpan BoundaryPadding { get; init; } = TimeSpan.FromMilliseconds(120);
}
