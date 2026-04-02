using HsAsrDictation.Logging;
using NAudio.Wave;

namespace HsAsrDictation.Audio;

public sealed class WaveInAudioCaptureService : IAudioCaptureService
{
    private const int MaxDurationSeconds = 30;
    private readonly object _syncRoot = new();
    private readonly LocalLogService _logger;
    private readonly List<float> _samples = [];
    private WaveInEvent? _waveIn;
    private TaskCompletionSource? _stopCompletion;

    public WaveInAudioCaptureService(LocalLogService logger)
    {
        _logger = logger;
    }

    public bool IsRecording { get; private set; }

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
            _waveIn = new WaveInEvent
            {
                DeviceNumber = ResolveDeviceNumber(preferredDeviceName),
                BufferMilliseconds = 40,
                NumberOfBuffers = 3,
                WaveFormat = new WaveFormat(16000, 16, 1)
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
                return new RecordedAudio(Array.Empty<float>(), TimeSpan.Zero);
            }

            stopCompletion = _stopCompletion;
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
            return new RecordedAudio(copy, TimeSpan.FromSeconds(copy.Length / 16000d));
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
        lock (_syncRoot)
        {
            for (var i = 0; i < e.BytesRecorded; i += 2)
            {
                if (_samples.Count >= 16000 * MaxDurationSeconds)
                {
                    _waveIn?.StopRecording();
                    break;
                }

                var sample = BitConverter.ToInt16(e.Buffer, i);
                _samples.Add(sample / 32768f);
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_syncRoot)
        {
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
            CleanupWaveIn();
        }
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
