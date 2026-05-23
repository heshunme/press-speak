using HsAsrDictation.Logging;
using NAudio.Wave;

namespace HsAsrDictation.Audio;

public sealed class WaveInAudioCaptureService : IAudioCaptureService
{
    private const int SampleRate = 16000;
    private static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(5);
    private static readonly int MaxSampleCount = (int)(SampleRate * MaxDuration.TotalSeconds);
    private readonly object _syncRoot = new();
    private readonly LocalLogService _logger;
    private readonly List<float> _samples = [];
    private WaveInEvent? _waveIn;
    private TaskCompletionSource? _stopCompletion;
    private RecordedAudio _lastRecordedAudio = new(Array.Empty<float>(), TimeSpan.Zero);
    private AudioCaptureStopReason _pendingStopReason = AudioCaptureStopReason.UserRequested;

    public WaveInAudioCaptureService(LocalLogService logger)
    {
        _logger = logger;
    }

    public bool IsRecording { get; private set; }

    public event EventHandler<AudioChunkAvailableEventArgs>? AudioChunkAvailable;

    public event EventHandler<AudioCaptureStoppedEventArgs>? RecordingStopped;

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        for (var i = 0; i < WaveIn.DeviceCount; i++)
        {
            var caps = WaveIn.GetCapabilities(i);
            devices.Add(new AudioDeviceInfo
            {
                DeviceNumber = i,
                ProductName = caps.ProductName
            });
        }

        return devices;
    }

    public Task StartAsync(string? preferredDeviceName, CancellationToken ct = default)
    {
        lock (_syncRoot)
        {
            if (IsRecording)
            {
                throw new InvalidOperationException("录音已在进行中。");
            }

            _samples.Clear();
            _stopCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _lastRecordedAudio = new RecordedAudio(Array.Empty<float>(), TimeSpan.Zero);
            _pendingStopReason = AudioCaptureStopReason.UserRequested;
            _waveIn = new WaveInEvent
            {
                DeviceNumber = ResolveDeviceNumber(preferredDeviceName),
                BufferMilliseconds = 40,
                NumberOfBuffers = 3,
                WaveFormat = new WaveFormat(SampleRate, 16, 1)
            };

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _waveIn.StartRecording();

            IsRecording = true;
            _logger.Info("录音开始。");
            return Task.CompletedTask;
        }
    }

    public async Task<RecordedAudio> StopAsync(CancellationToken ct = default)
    {
        TaskCompletionSource? stopCompletion;

        lock (_syncRoot)
        {
            if (!IsRecording || _waveIn is null)
            {
                return _lastRecordedAudio;
            }

            stopCompletion = _stopCompletion;
            _pendingStopReason = AudioCaptureStopReason.UserRequested;
            _waveIn.StopRecording();
        }

        if (stopCompletion is not null)
        {
            using var registration = ct.Register(() => stopCompletion.TrySetCanceled(ct));
            await stopCompletion.Task;
        }

        lock (_syncRoot)
        {
            var copy = _samples.ToArray();
            _lastRecordedAudio = new RecordedAudio(copy, TimeSpan.FromSeconds(copy.Length / (double)SampleRate));
            return _lastRecordedAudio;
        }
    }

    public void Dispose()
    {
        CleanupWaveIn();
    }

    private int ResolveDeviceNumber(string? preferredDeviceName)
    {
        if (string.IsNullOrWhiteSpace(preferredDeviceName))
        {
            return 0;
        }

        for (var i = 0; i < WaveIn.DeviceCount; i++)
        {
            var caps = WaveIn.GetCapabilities(i);
            if (string.Equals(caps.ProductName, preferredDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        float[]? chunkSamples = null;
        var samplesWritten = 0;
        var reachedMaxDuration = false;

        lock (_syncRoot)
        {
            chunkSamples = new float[e.BytesRecorded / 2];

            for (var i = 0; i < e.BytesRecorded; i += 2)
            {
                if (_samples.Count >= MaxSampleCount)
                {
                    _pendingStopReason = AudioCaptureStopReason.MaxDurationReached;
                    _waveIn?.StopRecording();
                    reachedMaxDuration = true;
                    break;
                }

                var sample = BitConverter.ToInt16(e.Buffer, i);
                var normalizedSample = sample / 32768f;
                _samples.Add(normalizedSample);
                chunkSamples[samplesWritten++] = normalizedSample;
            }
        }

        if (reachedMaxDuration)
        {
            _logger.Warn($"录音达到单次上限（{MaxDuration.TotalMinutes:0} 分钟），将自动结束当前听写。");
        }

        if (chunkSamples is not null && samplesWritten > 0)
        {
            if (samplesWritten != chunkSamples.Length)
            {
                Array.Resize(ref chunkSamples, samplesWritten);
            }

            AudioChunkAvailable?.Invoke(this, new AudioChunkAvailableEventArgs(chunkSamples));
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        AudioCaptureStoppedEventArgs? stoppedEventArgs;
        EventHandler<AudioCaptureStoppedEventArgs>? recordingStopped;

        lock (_syncRoot)
        {
            var copy = _samples.ToArray();
            _lastRecordedAudio = new RecordedAudio(copy, TimeSpan.FromSeconds(copy.Length / (double)SampleRate));
            var stopReason = e.Exception is null ? _pendingStopReason : AudioCaptureStopReason.Faulted;
            _pendingStopReason = AudioCaptureStopReason.UserRequested;

            if (e.Exception is not null)
            {
                _logger.Error("录音停止时发生异常。", e.Exception);
                _stopCompletion?.TrySetException(e.Exception);
            }
            else
            {
                _logger.Info("录音结束。");
                _stopCompletion?.TrySetResult();
            }

            IsRecording = false;
            stoppedEventArgs = new AudioCaptureStoppedEventArgs(_lastRecordedAudio, stopReason, e.Exception);
            recordingStopped = RecordingStopped;
            CleanupWaveIn();
        }

        recordingStopped?.Invoke(this, stoppedEventArgs);
    }

    private void CleanupWaveIn()
    {
        if (_waveIn is null)
        {
            return;
        }

        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.RecordingStopped -= OnRecordingStopped;
        _waveIn.Dispose();
        _waveIn = null;
    }
}
