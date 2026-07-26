namespace HsAsrDictation.Audio;

public interface IAudioCaptureService : IDisposable
{
    bool IsRecording { get; }

    event EventHandler<AudioChunkAvailableEventArgs>? AudioChunkAvailable;

    event EventHandler<AudioCaptureStoppedEventArgs>? RecordingStopped;

    IReadOnlyList<AudioDeviceInfo> GetInputDevices();

    Task StartAsync(string? preferredDeviceName, CancellationToken ct = default);

    Task<RecordedAudio> StopAsync(CancellationToken ct = default);
}
