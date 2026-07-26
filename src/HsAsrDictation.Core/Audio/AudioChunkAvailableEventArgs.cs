namespace HsAsrDictation.Audio;

public sealed class AudioChunkAvailableEventArgs : EventArgs
{
    public AudioChunkAvailableEventArgs(float[] samples)
    {
        Samples = samples;
    }

    public float[] Samples { get; }
}
