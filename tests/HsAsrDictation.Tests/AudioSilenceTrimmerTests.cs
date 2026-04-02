using HsAsrDictation.Audio;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class AudioSilenceTrimmerTests
{
    [Fact]
    public void Trim_RemovesLeadingAndTrailingSilence()
    {
        var samples = new List<float>();
        samples.AddRange(Enumerable.Repeat(0f, 1600));
        samples.AddRange(Enumerable.Repeat(0.2f, 3200));
        samples.AddRange(Enumerable.Repeat(0f, 1600));

        var trimmed = AudioSilenceTrimmer.Trim(samples.ToArray(), 16000);

        Assert.NotEmpty(trimmed);
        Assert.All(trimmed, sample => Assert.True(Math.Abs(sample) >= 0.015f));
    }
}
