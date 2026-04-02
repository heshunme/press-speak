namespace HsAsrDictation.Audio;

public sealed class RecordedAudio
{
    public RecordedAudio(float[] samples, TimeSpan duration)
    {
        Samples = samples;
        Duration = duration;
    }

    public float[] Samples { get; }

    public TimeSpan Duration { get; }
}
