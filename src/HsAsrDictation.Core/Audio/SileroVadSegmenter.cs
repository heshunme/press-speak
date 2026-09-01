using SherpaOnnx;

namespace HsAsrDictation.Audio;

/// <summary>
/// 基于 sherpa-onnx Silero VAD 的分段器。
///
/// 两条实测得来的约束（见 docs/vad-segmented-decoding-design.md 第 4 节）：
/// 1. <c>MaxSpeechDuration</c> 不可靠（实测 maxSpeech=8 时 14 s 连续语音一段没切），
///    因此硬上限由本类自己调 <see cref="Flush"/> 实现，配置里把它设为极大值让原生层不要插手。
/// 2. 构造时的 buffer 容量按录音时长上限给足；不够时原生层会自动扩容并向 stderr 打
///    "Overflow!"，不丢数据但产生噪声输出。
/// </summary>
public sealed class SileroVadSegmenter : IAudioSegmenter
{
    public const int SampleRate = 16000;
    private const int WindowSize = 512;

    // 交给原生层的上限设成足够大，使强制切分完全由 MaxOpenSegmentSamples 掌握。
    private const float NativeMaxSpeechDurationSeconds = 100000f;

    private readonly VoiceActivityDetector _vad;
    private readonly long _maxOpenSegmentSamples;
    private readonly Queue<AudioSegment> _pending = new();

    private long _fedSamples;
    private long _openSegmentStartSample = -1;
    private bool _speechActive;
    private bool _disposed;

    public SileroVadSegmenter(
        string modelPath,
        TimeSpan minSilenceDuration,
        TimeSpan minSpeechDuration,
        TimeSpan maxOpenSegmentDuration,
        TimeSpan bufferCapacity)
    {
        var config = new VadModelConfig();
        config.SileroVad.Model = modelPath;
        config.SileroVad.Threshold = 0.5f;
        config.SileroVad.MinSilenceDuration = (float)minSilenceDuration.TotalSeconds;
        config.SileroVad.MinSpeechDuration = (float)minSpeechDuration.TotalSeconds;
        config.SileroVad.WindowSize = WindowSize;
        config.SileroVad.MaxSpeechDuration = NativeMaxSpeechDurationSeconds;
        config.SampleRate = SampleRate;
        config.NumThreads = 1;
        config.Provider = "cpu";
        config.Debug = 0;

        _maxOpenSegmentSamples = (long)(maxOpenSegmentDuration.TotalSeconds * SampleRate);

        // 容量再留 2 s 余量，避免恰好等于上限时触发原生扩容日志。
        var bufferSeconds = (float)bufferCapacity.TotalSeconds + 2f;
        _vad = new VoiceActivityDetector(config, bufferSeconds);
    }

    public bool IsSpeechActive => _speechActive;

    public void AcceptChunk(float[] samples)
    {
        ThrowIfDisposed();

        if (samples.Length == 0)
        {
            return;
        }

        _vad.AcceptWaveform(samples);
        _fedSamples += samples.Length;

        var speechNow = _vad.IsSpeechDetected();
        if (speechNow && _openSegmentStartSample < 0)
        {
            _openSegmentStartSample = _fedSamples;
        }

        _speechActive = speechNow;
        DrainNativeQueue();

        // 单段超过硬上限：主动封口，避免"一口气说很久"退化成整段解码。
        // 实测切点连续（前段 end == 后段 start），不会丢样本或重叠。
        if (speechNow &&
            _openSegmentStartSample >= 0 &&
            _fedSamples - _openSegmentStartSample >= _maxOpenSegmentSamples)
        {
            Flush();
        }
    }

    public bool TryDequeue(out AudioSegment segment)
    {
        ThrowIfDisposed();
        DrainNativeQueue();

        if (_pending.Count == 0)
        {
            segment = null!;
            return false;
        }

        segment = _pending.Dequeue();
        return true;
    }

    public void Flush()
    {
        ThrowIfDisposed();

        _vad.Flush();
        DrainNativeQueue();
        _speechActive = false;
        _openSegmentStartSample = -1;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pending.Clear();
        _vad.Dispose();
    }

    private void DrainNativeQueue()
    {
        while (!_vad.IsEmpty())
        {
            var native = _vad.Front();
            _vad.Pop();

            if (native.Samples.Length == 0)
            {
                continue;
            }

            _pending.Enqueue(new AudioSegment(native.Start, native.Samples));

            // 段已封口，下一次检测到语音时重新记开始位置。
            _openSegmentStartSample = -1;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SileroVadSegmenter));
        }
    }
}
