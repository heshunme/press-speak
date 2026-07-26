namespace HsAsrDictation.Services;

public enum DictationState
{
    Idle,
    Recording,
    Finalizing,
    Decoding,
    Inserting
}

public static class DictationStateExtensions
{
    public static string ToDisplayText(this DictationState state) =>
        state switch
        {
            DictationState.Idle => "就绪",
            DictationState.Recording => "录音中",
            DictationState.Finalizing => "结束录音",
            DictationState.Decoding => "识别中",
            DictationState.Inserting => "回写中",
            _ => "未知状态"
        };
}
