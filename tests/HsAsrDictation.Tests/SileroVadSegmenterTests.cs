using HsAsrDictation.Audio;
using HsAsrDictation.Models;
using Xunit;

namespace HsAsrDictation.Tests;

/// <summary>
/// 真 VAD 的集成测试。模型随发布包内置，测试工程输出目录不含它，
/// 因此模型缺失时整类跳过（本地可用 VAD_MODEL_PATH 指向仓库内的模型文件运行）。
/// </summary>
public sealed class SileroVadSegmenterTests
{
    private const int SampleRate = SileroVadSegmenter.SampleRate;

    private static string? ResolveModelPath()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("VAD_MODEL_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
        {
            return fromEnvironment;
        }

        // 从测试输出目录向上找仓库根，再定位内置模型。
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "HsAsrDictation",
                "Resources",
                "Vad",
                VadModelLocator.ModelFileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static SileroVadSegmenter? TryCreate(TimeSpan maxOpenSegment)
    {
        var modelPath = ResolveModelPath();
        if (modelPath is null)
        {
            return null;
        }

        return new SileroVadSegmenter(
            modelPath,
            minSilenceDuration: TimeSpan.FromMilliseconds(500),
            minSpeechDuration: TimeSpan.FromMilliseconds(100),
            maxOpenSegmentDuration: maxOpenSegment,
            bufferCapacity: TimeSpan.FromSeconds(60));
    }

    private static float[] Silence(double seconds, Random random)
    {
        var samples = new float[(int)(SampleRate * seconds)];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(random.NextDouble() * 4e-4 - 2e-4);
        }

        return samples;
    }

    private static float[] Speech(double seconds, Random random, ref double phase)
    {
        var samples = new float[(int)(SampleRate * seconds)];
        for (var i = 0; i < samples.Length; i++)
        {
            phase += 2 * Math.PI * (150 + 60 * Math.Sin(i / 2000.0)) / SampleRate;
            samples[i] = (float)(
                0.3 * Math.Sin(phase) +
                0.12 * Math.Sin(phase * 2.7) +
                (random.NextDouble() - 0.5) * 0.04);
        }

        return samples;
    }

    private static List<AudioSegment> FeedInChunks(IAudioSegmenter segmenter, float[] samples)
    {
        var collected = new List<AudioSegment>();
        const int chunkSize = 640; // 与 WaveInAudioCaptureService 的 40 ms 缓冲一致

        for (var offset = 0; offset < samples.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, samples.Length - offset);
            var chunk = new float[length];
            Array.Copy(samples, offset, chunk, 0, length);
            segmenter.AcceptChunk(chunk);

            while (segmenter.TryDequeue(out var segment))
            {
                collected.Add(segment);
            }
        }

        return collected;
    }

    [Fact]
    public void AcceptChunk_SplitsOnPause_AndReportsMonotonicOffsets()
    {
        using var segmenter = TryCreate(TimeSpan.FromSeconds(30));
        if (segmenter is null)
        {
            return;
        }

        var random = new Random(42);
        var phase = 0d;
        var audio = new List<float>();
        audio.AddRange(Silence(1.0, random));
        audio.AddRange(Speech(2.0, random, ref phase));
        audio.AddRange(Silence(1.5, random));
        audio.AddRange(Speech(1.2, random, ref phase));
        audio.AddRange(Silence(1.0, random));

        var segments = FeedInChunks(segmenter, audio.ToArray());
        segmenter.Flush();
        while (segmenter.TryDequeue(out var tail))
        {
            segments.Add(tail);
        }

        Assert.Equal(2, segments.Count);
        Assert.True(segments[0].StartSample < segments[1].StartSample);
        Assert.True(segments[0].EndSampleExclusive <= segments[1].StartSample);

        // 段长应接近注入的语音时长（VAD 边界会略有出入）。
        Assert.InRange(segments[0].Samples.Length / (double)SampleRate, 1.5, 2.5);
        Assert.InRange(segments[1].Samples.Length / (double)SampleRate, 0.7, 1.7);
    }

    [Fact]
    public void Flush_ClosesOpenSegment_WhenSpeechRunsToTheEnd()
    {
        using var segmenter = TryCreate(TimeSpan.FromSeconds(30));
        if (segmenter is null)
        {
            return;
        }

        var random = new Random(7);
        var phase = 0d;
        var audio = new List<float>();
        audio.AddRange(Silence(0.6, random));
        audio.AddRange(Speech(2.0, random, ref phase));

        var segments = FeedInChunks(segmenter, audio.ToArray());

        // 语音持续到最后，未封口，此时队列应为空且报告正在说话。
        Assert.Empty(segments);
        Assert.True(segmenter.IsSpeechActive);

        segmenter.Flush();

        Assert.True(segmenter.TryDequeue(out var flushed));
        Assert.InRange(flushed.Samples.Length / (double)SampleRate, 1.5, 2.5);
        Assert.False(segmenter.IsSpeechActive);
    }

    [Fact]
    public void AcceptChunk_EnforcesHardCap_OnContinuousSpeech()
    {
        using var segmenter = TryCreate(TimeSpan.FromSeconds(4));
        if (segmenter is null)
        {
            return;
        }

        var random = new Random(7);
        var phase = 0d;
        var audio = new List<float>();
        audio.AddRange(Silence(0.5, random));
        audio.AddRange(Speech(14.0, random, ref phase));
        audio.AddRange(Silence(1.0, random));

        var segments = FeedInChunks(segmenter, audio.ToArray());
        segmenter.Flush();
        while (segmenter.TryDequeue(out var tail))
        {
            segments.Add(tail);
        }

        // 14 s 连续语音必须被硬上限切成多段，而不是退化为一整段。
        Assert.True(segments.Count >= 3, $"期望至少 3 段，实际 {segments.Count} 段。");
        Assert.All(segments, segment =>
            Assert.True(
                segment.Samples.Length / (double)SampleRate < 6.0,
                $"段长 {segment.Samples.Length / (double)SampleRate:F2}s 超出硬上限预期。"));

        // 切点必须连续：不丢样本也不重叠。
        for (var i = 1; i < segments.Count; i++)
        {
            Assert.True(segments[i - 1].EndSampleExclusive <= segments[i].StartSample);
        }
    }

    [Fact]
    public void Flush_AllowsContinuedUse_AndKeepsOffsetsAdvancing()
    {
        using var segmenter = TryCreate(TimeSpan.FromSeconds(30));
        if (segmenter is null)
        {
            return;
        }

        var random = new Random(11);
        var phase = 0d;

        var first = new List<float>();
        first.AddRange(Silence(0.3, random));
        first.AddRange(Speech(2.0, random, ref phase));
        FeedInChunks(segmenter, first.ToArray());
        segmenter.Flush();

        Assert.True(segmenter.TryDequeue(out var firstSegment));

        var second = new List<float>();
        second.AddRange(Speech(1.5, random, ref phase));
        second.AddRange(Silence(1.0, random));
        var afterFlush = FeedInChunks(segmenter, second.ToArray());
        segmenter.Flush();
        while (segmenter.TryDequeue(out var tail))
        {
            afterFlush.Add(tail);
        }

        Assert.NotEmpty(afterFlush);
        Assert.True(afterFlush[0].StartSample >= firstSegment.EndSampleExclusive);
    }
}
