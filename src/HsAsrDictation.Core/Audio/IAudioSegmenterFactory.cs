namespace HsAsrDictation.Audio;

/// <summary>
/// 按会话创建分段器。返回 null 表示分段能力不可用（例如内置模型缺失），
/// 调用方据此回落到整段解码，而不是让听写失败。
/// </summary>
public interface IAudioSegmenterFactory
{
    IAudioSegmenter? TryCreate(TimeSpan bufferCapacity);
}
