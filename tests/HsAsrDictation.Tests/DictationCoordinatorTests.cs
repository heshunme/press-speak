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

public sealed class DictationCoordinatorTests
{
    [Fact]
    public async Task FinalizeRecordingAfterHotkeyReleaseAsync_DelaysStopUntilTailDuration()
    {
        using var harness = new CoordinatorHarness(TimeSpan.FromMilliseconds(120));

        await harness.Coordinator.BeginRecordingAsync();
        await harness.Coordinator.FinalizeRecordingAfterHotkeyReleaseAsync();

        Assert.Equal(0, harness.AudioCapture.StopCallCount);

        await Task.Delay(50);
        Assert.Equal(0, harness.AudioCapture.StopCallCount);

        await harness.AudioCapture.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await harness.TextInsertion.InsertCalled.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, harness.AudioCapture.StopCallCount);
        Assert.Equal(new[] { "尾字保留" }, harness.TextInsertion.InsertedTexts);
    }

    [Fact]
    public async Task BeginRecordingAsync_CancelsPendingTailStopAndRequiresNextReleaseToFinalize()
    {
        using var harness = new CoordinatorHarness(TimeSpan.FromMilliseconds(120));

        await harness.Coordinator.BeginRecordingAsync();
        await harness.Coordinator.FinalizeRecordingAfterHotkeyReleaseAsync();
        await Task.Delay(50);

        await harness.Coordinator.BeginRecordingAsync();
        await Task.Delay(160);

        Assert.Equal(0, harness.AudioCapture.StopCallCount);
        Assert.Empty(harness.TextInsertion.InsertedTexts);

        await harness.Coordinator.FinalizeRecordingAfterHotkeyReleaseAsync();
        await harness.AudioCapture.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await harness.TextInsertion.InsertCalled.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, harness.AudioCapture.StopCallCount);
        Assert.Equal(new[] { "尾字保留" }, harness.TextInsertion.InsertedTexts);
    }

    [Fact]
    public async Task ToggleRecordingAsync_StopsImmediately_WhenTailStopIsPending()
    {
        using var harness = new CoordinatorHarness(TimeSpan.FromMilliseconds(250));

        await harness.Coordinator.BeginRecordingAsync();
        await harness.Coordinator.FinalizeRecordingAfterHotkeyReleaseAsync();

        await harness.Coordinator.ToggleRecordingAsync();

        Assert.Equal(1, harness.AudioCapture.StopCallCount);
        await Task.Delay(300);
        Assert.Equal(1, harness.AudioCapture.StopCallCount);
    }

    [Fact]
    public async Task FinalizeRecordingAfterHotkeyReleaseAsync_DoesNotQueueMultipleStops()
    {
        using var harness = new CoordinatorHarness(TimeSpan.FromMilliseconds(120));

        await harness.Coordinator.BeginRecordingAsync();
        await harness.Coordinator.FinalizeRecordingAfterHotkeyReleaseAsync();
        await harness.Coordinator.FinalizeRecordingAfterHotkeyReleaseAsync();

        await harness.AudioCapture.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await harness.TextInsertion.InsertCalled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(80);

        Assert.Equal(1, harness.AudioCapture.StopCallCount);
        Assert.Equal(new[] { "尾字保留" }, harness.TextInsertion.InsertedTexts);
    }

    private sealed class CoordinatorHarness : IDisposable
    {
        private readonly string _tempDirectory;

        public CoordinatorHarness(TimeSpan tailDuration)
        {
            _tempDirectory = Path.Combine(
                Path.GetTempPath(),
                "hs-asr-tests",
                $"{nameof(DictationCoordinatorTests)}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDirectory);

            Logger = new LocalLogService(_tempDirectory);
            Settings = new SettingsService(Path.Combine(_tempDirectory, "settings.json"), Logger);
            Settings.Save(new AppSettings
            {
                RecognitionMode = RecognitionMode.NonStreaming,
                EnableStreamingPreview = false,
                EnablePostProcessingRules = false
            });

            AudioCapture = new FakeAudioCaptureService();
            ModelProvisioning = new FakeModelProvisioningService();
            PunctuationModelProvisioning = new FakePunctuationModelProvisioningService();
            AsrEngine = new FakeAsrEngine();
            StreamingAsrEngine = new FakeStreamingAsrEngine();
            PunctuationService = new FakePunctuationService();
            PostProcessingService = new FakePostProcessingService();
            ForegroundContextService = new FakeForegroundContextService();
            TextInsertion = new FakeTextInsertionService();
            NotificationService = new FakeNotificationService();

            Coordinator = new DictationCoordinator(
                Settings,
                AudioCapture,
                ModelProvisioning,
                PunctuationModelProvisioning,
                AsrEngine,
                StreamingAsrEngine,
                PunctuationService,
                PostProcessingService,
                ForegroundContextService,
                TextInsertion,
                NotificationService,
                Logger,
                tailDuration);
        }

        public DictationCoordinator Coordinator { get; }

        public FakeAudioCaptureService AudioCapture { get; }

        public FakeTextInsertionService TextInsertion { get; }

        private SettingsService Settings { get; }

        private LocalLogService Logger { get; }

        private FakeModelProvisioningService ModelProvisioning { get; }

        private FakePunctuationModelProvisioningService PunctuationModelProvisioning { get; }

        private FakeAsrEngine AsrEngine { get; }

        private FakeStreamingAsrEngine StreamingAsrEngine { get; }

        private FakePunctuationService PunctuationService { get; }

        private FakePostProcessingService PostProcessingService { get; }

        private FakeForegroundContextService ForegroundContextService { get; }

        private FakeNotificationService NotificationService { get; }

        public void Dispose()
        {
            AudioCapture.Dispose();
            AsrEngine.Dispose();
            StreamingAsrEngine.Dispose();
            PunctuationService.Dispose();
            Logger.Dispose();
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private sealed class FakeAudioCaptureService : IAudioCaptureService
    {
        private static readonly float[] Samples = Enumerable.Repeat(0.2f, 3200).ToArray();

        public int StopCallCount { get; private set; }

        public bool IsRecording { get; private set; }

        public TaskCompletionSource StopCalled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<AudioChunkAvailableEventArgs>? AudioChunkAvailable;

        public IReadOnlyList<AudioDeviceInfo> GetInputDevices() => [];

        public Task StartAsync(string? preferredDeviceName, CancellationToken ct = default)
        {
            IsRecording = true;
            _ = AudioChunkAvailable;
            return Task.CompletedTask;
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
                Text = "尾字保留"
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

        public IStreamingAsrSession CreateSession() => new FakeStreamingAsrSession();

        public void Dispose()
        {
        }
    }

    private sealed class FakeStreamingAsrSession : IStreamingAsrSession
    {
        public ValueTask AcceptAudioAsync(float[] pcm16kMonoChunk, CancellationToken ct = default) => ValueTask.CompletedTask;

        public StreamingAsrResult GetCurrentResult() => new();

        public ValueTask<StreamingAsrResult> CompleteAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(new StreamingAsrResult());

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
        public void Info(string title, string message)
        {
        }

        public void Warn(string title, string message)
        {
        }

        public void Error(string title, string message)
        {
        }
    }
}
