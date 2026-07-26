using System.Collections.ObjectModel;
using HsAsrDictation.Asr;
using HsAsrDictation.Audio;
using HsAsrDictation.Foreground;
using HsAsrDictation.Insertion;
using HsAsrDictation.Logging;
using HsAsrDictation.Models;
using HsAsrDictation.Notifications;
using HsAsrDictation.PostProcessing.Abstractions;
using HsAsrDictation.PostProcessing.Engine;
using HsAsrDictation.PostProcessing.Models;
using HsAsrDictation.Services;
using HsAsrDictation.Settings;
using Xunit;

namespace HsAsrDictation.Tests;

/// <summary>针对会话生命周期竞态的测试：启动窗口、启动失败恢复、设置快照。</summary>
public sealed class DictationCoordinatorSessionTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly LocalLogService _logger;
    private readonly SettingsService _settings;
    private readonly ControllableAudioCaptureService _audioCapture = new();
    private readonly FakeTextInsertionService _textInsertion = new();
    private readonly FakeNotificationService _notifications = new();

    public DictationCoordinatorSessionTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "hs-asr-tests",
            $"{nameof(DictationCoordinatorSessionTests)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _logger = new LocalLogService(_tempDirectory);
        _settings = new SettingsService(Path.Combine(_tempDirectory, "settings.json"), _logger);
        _settings.Save(new AppSettings
        {
            RecognitionMode = RecognitionMode.NonStreaming,
            EnableStreamingPreview = false,
            EnablePostProcessingRules = false
        });
    }

    public void Dispose()
    {
        _logger.Dispose();
        Directory.Delete(_tempDirectory, recursive: true);
    }

    private DictationCoordinator CreateCoordinator(TimeSpan? tailDuration) =>
        new(
            _settings,
            _audioCapture,
            new FakeModelProvisioningService(),
            new FakePunctuationModelProvisioningService(),
            new FakeAsrEngine(),
            new FakeStreamingAsrEngine(),
            new FakePunctuationService(),
            new FakePostProcessingService(),
            new FakeForegroundContextService(),
            _textInsertion,
            _notifications,
            _logger,
            tailDuration);

    [Fact]
    public async Task HotkeyRelease_DuringAudioStart_StillStopsRecording()
    {
        var coordinator = CreateCoordinator(TimeSpan.FromMilliseconds(20));
        _audioCapture.BlockNextStart();

        var beginTask = coordinator.BeginRecordingAsync();
        await coordinator.FinalizeRecordingAfterHotkeyReleaseAsync();

        // 尾录到期后结束流程会等待启动完成，而不是把释放丢掉。
        await Task.Delay(80);
        Assert.Equal(0, _audioCapture.StopCallCount);

        _audioCapture.ReleaseStart();
        await beginTask;

        await _audioCapture.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await _textInsertion.InsertCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, _audioCapture.StopCallCount);
        Assert.Equal(new[] { "识别结果" }, _textInsertion.InsertedTexts);
    }

    [Fact]
    public async Task BeginRecording_WhenAudioStartFails_ReturnsToIdleAndCanRecordAgain()
    {
        var coordinator = CreateCoordinator(TimeSpan.FromMilliseconds(20));
        var statuses = new Collection<DictationState>();
        coordinator.StateChanged += (_, status) => statuses.Add(status.State);

        _audioCapture.FailNextStart(new InvalidOperationException("设备被占用"));
        await coordinator.BeginRecordingAsync();

        Assert.Contains(_notifications.ErrorMessages, message => message.Contains("录音启动失败", StringComparison.Ordinal));
        Assert.Equal(DictationState.Idle, statuses[^1]);
        Assert.Equal(0, _audioCapture.StopCallCount);

        await coordinator.BeginRecordingAsync();
        await coordinator.FinalizeRecordingAfterHotkeyReleaseAsync();

        await _audioCapture.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await _textInsertion.InsertCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(new[] { "识别结果" }, _textInsertion.InsertedTexts);
    }

    [Fact]
    public async Task Session_UsesSettingsSnapshot_ForTailDuration()
    {
        var coordinator = CreateCoordinator(tailDuration: null);
        _settings.Save(new AppSettings
        {
            RecognitionMode = RecognitionMode.NonStreaming,
            EnableStreamingPreview = false,
            EnablePostProcessingRules = false,
            HotkeyReleaseTailDurationMilliseconds = 100
        });

        await coordinator.BeginRecordingAsync();

        // 会话进行中把尾录时长改大，本会话应仍使用开始时的快照值。
        _settings.Save(new AppSettings
        {
            RecognitionMode = RecognitionMode.NonStreaming,
            EnableStreamingPreview = false,
            EnablePostProcessingRules = false,
            HotkeyReleaseTailDurationMilliseconds = 4000
        });

        await coordinator.FinalizeRecordingAfterHotkeyReleaseAsync();
        await _audioCapture.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, _audioCapture.StopCallCount);
    }

    private sealed class ControllableAudioCaptureService : IAudioCaptureService
    {
        private static readonly float[] Samples = Enumerable.Repeat(0.2f, 3200).ToArray();
        private TaskCompletionSource? _startGate;
        private Exception? _nextStartFailure;

        public int StopCallCount { get; private set; }

        public bool IsRecording { get; private set; }

        public TaskCompletionSource StopCalled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<AudioChunkAvailableEventArgs>? AudioChunkAvailable;

        public event EventHandler<AudioCaptureStoppedEventArgs>? RecordingStopped;

        public IReadOnlyList<AudioDeviceInfo> GetInputDevices() => [];

        public void BlockNextStart() =>
            _startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseStart() => _startGate?.TrySetResult();

        public void FailNextStart(Exception exception) => _nextStartFailure = exception;

        public async Task StartAsync(string? preferredDeviceName, CancellationToken ct = default)
        {
            if (_nextStartFailure is { } failure)
            {
                _nextStartFailure = null;
                throw failure;
            }

            if (_startGate is { } gate)
            {
                await gate.Task;
            }

            IsRecording = true;
            _ = AudioChunkAvailable;
            _ = RecordingStopped;
        }

        public Task<RecordedAudio> StopAsync(CancellationToken ct = default)
        {
            StopCallCount++;
            IsRecording = false;
            StopCalled.TrySetResult();
            return Task.FromResult(new RecordedAudio(Samples, TimeSpan.FromMilliseconds(200)));
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeModelProvisioningService : IModelProvisioningService
    {
        public Task<ModelReadyResult> EnsureReadyAsync(AsrModelKind kind, bool downloadIfMissing, CancellationToken ct = default) =>
            Task.FromResult(new ModelReadyResult { IsReady = true, ModelDirectory = "ready" });

        public Task<ModelReadyResult> DownloadAsync(AsrModelKind kind, CancellationToken ct = default) =>
            Task.FromResult(new ModelReadyResult { IsReady = true, ModelDirectory = "ready" });
    }

    private sealed class FakePunctuationModelProvisioningService : IPunctuationModelProvisioningService
    {
        public Task<ModelReadyResult> EnsureReadyAsync(bool downloadIfMissing, CancellationToken ct = default) =>
            Task.FromResult(new ModelReadyResult { IsReady = true, ModelDirectory = "ready" });

        public Task<ModelReadyResult> DownloadAsync(CancellationToken ct = default) =>
            Task.FromResult(new ModelReadyResult { IsReady = true, ModelDirectory = "ready" });
    }

    private sealed class FakeAsrEngine : IAsrEngine
    {
        public bool IsReady => true;

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public void Unload()
        {
        }

        public Task<AsrResult> TranscribeAsync(float[] pcm16kMono, CancellationToken ct = default) =>
            Task.FromResult(new AsrResult
            {
                Success = true,
                Text = "识别结果"
            });

        public void Dispose()
        {
        }
    }

    private sealed class FakeStreamingAsrEngine : IStreamingAsrEngine
    {
        public bool IsReady => true;

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public void Unload()
        {
        }

        public IStreamingAsrSession CreateSession() => throw new InvalidOperationException("非流式模式不应创建流式会话。");

        public void Dispose()
        {
        }
    }

    private sealed class FakePunctuationService : IPunctuationService
    {
        public bool IsEnabled => false;

        public bool IsReady => true;

        public void Reload(PunctuationRuntimeOptions options)
        {
        }

        public string TryAddPunctuation(string text) => text;

        public void Dispose()
        {
        }
    }

    private sealed class FakePostProcessingService : IPostProcessingService
    {
        public string TryProcess(string input, RuleExecutionContext context) => input;

        public PostProcessingTraceResult TestProcess(string input, RuleExecutionContext context) =>
            new()
            {
                Input = input,
                Output = input
            };

        public PostProcessingTraceResult TestProcess(PostProcessingConfig config, string input, RuleExecutionContext context) =>
            new()
            {
                Input = input,
                Output = input
            };
    }

    private sealed class FakeForegroundContextService : IForegroundContextService
    {
        private static readonly ForegroundContext Context = new()
        {
            ProcessName = "tests",
            WindowTitle = "tests"
        };

        public ForegroundContext Capture() => Context;

        public bool Restore(ForegroundContext context) => true;
    }

    private sealed class FakeTextInsertionService : ITextInsertionService
    {
        public Collection<string> InsertedTexts { get; } = [];

        public TaskCompletionSource InsertCalled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<InsertionResult> InsertAsync(string text, ForegroundContext context, CancellationToken ct = default)
        {
            InsertedTexts.Add(text);
            InsertCalled.TrySetResult();
            return Task.FromResult(new InsertionResult
            {
                Success = true,
                Method = "test"
            });
        }
    }

    private sealed class FakeNotificationService : INotificationService
    {
        public Collection<string> ErrorMessages { get; } = [];

        public void Info(string title, string message)
        {
        }

        public void Warn(string title, string message)
        {
        }

        public void Error(string title, string message)
        {
            ErrorMessages.Add(message);
        }
    }
}
