namespace HsAsrDictation.Audio;

public static class AudioSilenceTrimmer
{
    public static float[] Trim(float[] samples, int sampleRate, float threshold = 0.015f, int minWindowMs = 50)
    {
        if (samples.Length == 0)
        {
            return samples;
        }

        var windowSize = Math.Max(1, sampleRate * minWindowMs / 1000);
        var start = FindBoundary(samples, windowSize, threshold, fromStart: true);
        var endExclusive = FindBoundary(samples, windowSize, threshold, fromStart: false);

        if (endExclusive <= start)
        {
            return Array.Empty<float>();
        }

        var trimmed = new float[endExclusive - start];
        Array.Copy(samples, start, trimmed, 0, trimmed.Length);
        return trimmed;
    }

    private static int FindBoundary(float[] samples, int windowSize, float threshold, bool fromStart)
    {
        if (fromStart)
        {
            for (var index = 0; index < samples.Length; index += windowSize)
            {
                if (WindowHasSpeech(samples, index, Math.Min(windowSize, samples.Length - index), threshold))
                {
                    return index;
                }
            }

            return samples.Length;
        }

        for (var index = samples.Length - windowSize; index >= 0; index -= windowSize)
        {
            if (WindowHasSpeech(samples, index, Math.Min(windowSize, samples.Length - index), threshold))
            {
                return Math.Min(samples.Length, index + windowSize);
            }
        }

        return 0;
    }

    private static bool WindowHasSpeech(float[] samples, int offset, int length, float threshold)
    {
        for (var i = offset; i < offset + length; i++)
        {
            if (Math.Abs(samples[i]) >= threshold)
            {
                return true;
            }
        }

        return false;
    }
}
