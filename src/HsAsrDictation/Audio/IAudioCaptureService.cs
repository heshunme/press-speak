namespace HsAsrDictation.Audio;

public interface IAudioCaptureService : IDisposable
{
    bool IsRecording { get; }

    IReadOnlyList<AudioDeviceInfo> GetInputDevices();

    Task StartAsync(string? preferredDeviceName, CancellationToken ct = default);

    Task<RecordedAudio> StopAsync(CancellationToken ct = default);
}
