namespace HsAsrDictation.Audio;

public sealed class AudioCaptureStoppedEventArgs : EventArgs
{
    public AudioCaptureStoppedEventArgs(
        RecordedAudio audio,
        AudioCaptureStopReason reason,
        Exception? exception = null)
    {
        Audio = audio;
        Reason = reason;
        Exception = exception;
    }

    public RecordedAudio Audio { get; }

    public AudioCaptureStopReason Reason { get; }

    public Exception? Exception { get; }
}
