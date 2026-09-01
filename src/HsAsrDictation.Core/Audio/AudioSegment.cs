namespace HsAsrDictation.Audio;

/// <summary>
/// 一段由 VAD 判定为语音的音频。
/// </summary>
/// <param name="StartSample">相对录音起点的全局样本偏移，跨段单调递增。</param>
/// <param name="Samples">该段的音频样本（16 kHz mono）。</param>
public sealed record AudioSegment(long StartSample, float[] Samples)
{
    public long EndSampleExclusive => StartSample + Samples.Length;
}
