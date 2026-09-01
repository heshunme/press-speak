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

/// <summary>
/// 非流式 VAD 分段解码在协调器层面的行为：开关生效、分段文本写回、失败回落整段、自适应尾录。
/// 分段器用假实现注入（每块音频切成一段），不依赖真 VAD 模型。
/// </summary>
public sealed class DictationCoordinatorSegmentedTests
{
    [Fact]
    public async Task NonStreaming_WithSegmentedDecoding_WritesBackConcatenatedSegmentText()
    {
        using var harness = new Harness();

        await harness.Coordinator.BeginRecordingAsync();
        harness.AudioCapture.EmitChunk(16000);
        harness.AudioCapture.EmitChunk(16000);
        await harness.Coordinator.FinalizeRecordingAsync();
        await harness.TextInsertion.InsertCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // 两块音频 → 两段 → 拼接写回；整段解码不应被使用。
        Assert.Equal(new[] { "分段一分段二" }, harness.TextInsertion.InsertedTexts);
        Assert.Equal(0, harness.AsrEngine.WholeAudioCallCount);
        Assert.True(harness.SegmenterFactory.CreatedCount >= 1);
    }

    [Fact]
    public async Task NonStreaming_WhenToggleDisabled_UsesWholeAudioDecoding()
    {
        using var harness = new Harness();
        harness.SaveSettings(enableSegmented: false);

        await harness.Coordinator.BeginRecordingAsync();
        harness.AudioCapture.EmitChunk(16000);
        await harness.Coordinator.FinalizeRecordingAsync();
        await harness.TextInsertion.InsertCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, harness.SegmenterFactory.CreatedCount);
        Assert.Equal(1, harness.AsrEngine.WholeAudioCallCount);
        Assert.Equal(new[] { "整段结果" }, harness.TextInsertion.InsertedTexts);
    }

    [Fact]
    public async Task NonStreaming_WhenSegmenterUnavailable_FallsBackToWholeAudioDecoding()
    {
        using var harness = new Harness();
        harness.SegmenterFactory.ReturnNull = true;

        await harness.Coordinator.BeginRecordingAsync();
        harness.AudioCapture.EmitChunk(16000);
        await harness.Coordinator.FinalizeRecordingAsync();
        await harness.TextInsertion.InsertCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // 模型缺失不能让听写失败，必须照常写回整段解码结果。
        Assert.Equal(1, harness.AsrEngine.WholeAudioCallCount);
        Assert.Equal(new[] { "整段结果" }, harness.TextInsertion.InsertedTexts);
    }

    [Fact]
    public async Task NonStreaming_WhenSegmenterThrows_FallsBackToWholeAudioDecoding()
    {
        using var harness = new Harness();
        harness.SegmenterFactory.ThrowOnAccept = true;

        await harness.Coordinator.BeginRecordingAsync();
        harness.AudioCapture.EmitChunk(16000);
        await harness.Coordinator.FinalizeRecordingAsync();
        await harness.TextInsertion.InsertCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, harness.AsrEngine.WholeAudioCallCount);
        Assert.Equal(new[] { "整段结果" }, harness.TextInsertion.InsertedTexts);
    }

    [Fact]
    public async Task NonStreaming_WhenEverySegmentBatchFails_FallsBackToWholeAudioDecoding()
    {
        using var harness = new Harness();
        harness.AsrEngine.FailSegmentBatches = true;

        await harness.Coordinator.BeginRecordingAsync();
        harness.AudioCapture.EmitChunk(16000);
        await harness.Coordinator.FinalizeRecordingAsync();
        await harness.TextInsertion.InsertCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // 分段没产出文本时回落整段，避免"分段全失败 ⇒ 什么都没写回"。
        Assert.Equal(1, harness.AsrEngine.WholeAudioCallCount);
        Assert.Equal(new[] { "整段结果" }, harness.TextInsertion.InsertedTexts);
    }

    [Fact]
    public async Task HotkeyRelease_SkipsRemainingTail_WhenSpeechEndedAndQueueDrained()
    {
        // 尾录上限设得很长：只有自适应跳过生效，才可能在这个超时内完成。
        using var harness = new Harness(tailDuration: TimeSpan.FromSeconds(30));

        await harness.Coordinator.BeginRecordingAsync();
        harness.AudioCapture.EmitChunk(16000);
        await harness.Coordinator.FinalizeRecordingAfterHotkeyReleaseAsync();

        await harness.TextInsertion.InsertCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "分段一" }, harness.TextInsertion.InsertedTexts);
    }

    [Fact]
    public async Task HotkeyRelease_WaitsForTail_WhenStillSpeaking()
    {
        using var harness = new Harness(tailDuration: TimeSpan.FromMilliseconds(400));
        harness.SegmenterFactory.KeepSpeechActive = true;

        await harness.Coordinator.BeginRecordingAsync();
        harness.AudioCapture.EmitChunk(16000);
        await harness.Coordinator.FinalizeRecordingAfterHotkeyReleaseAsync();

        // 仍在说话 ⇒ 不能提前结束，200 ms 时尾录还没走完。
        await Task.Delay(200);
        Assert.Equal(0, harness.AudioCapture.StopCallCount);

        await harness.TextInsertion.InsertCalled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, harness.AudioCapture.StopCallCount);
    }

    [Fact]
    public async Task Hybrid_DoesNotUseSegmentedDecoding()
    {
        using var harness = new Harness();
        harness.SaveSettings(enableSegmented: true, mode: RecognitionMode.Hybrid);

        await harness.Coordinator.BeginRecordingAsync();
        harness.AudioCapture.EmitChunk(16000);
        await harness.Coordinator.FinalizeRecordingAsync();
        await harness.TextInsertion.InsertCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // 分段只作用于非流式；混合模式已有自己的边说边识别通路。
        Assert.Equal(0, harness.SegmenterFactory.CreatedCount);
        Assert.Equal(1, harness.AsrEngine.WholeAudioCallCount);
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly LocalLogService _logger;

        public Harness(TimeSpan? tailDuration = null)
        {
            _tempDirectory = Path.Combine(
                Path.GetTempPath(),
                "hs-asr-tests",
                $"{nameof(DictationCoordinatorSegmentedTests)}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDirectory);

            _logger = new LocalLogService(_tempDirectory);
            Settings = new SettingsService(Path.Combine(_tempDirectory, "settings.json"), _logger);
            SaveSettings(enableSegmented: true);

            AudioCapture = new FakeAudioCaptureService();
            AsrEngine = new FakeAsrEngine();
            SegmenterFactory = new FakeSegmenterFactory();
            TextInsertion = new FakeTextInsertionService();
            NotificationService = new FakeNotificationService();

            Coordinator = new DictationCoordinator(
                Settings,
                AudioCapture,
                new FakeModelProvisioningService(),
                new FakePunctuationModelProvisioningService(),
                AsrEngine,
                new FakeStreamingAsrEngine(),
                new FakePunctuationService(),
                new FakePostProcessingService(),
                new FakeForegroundContextService(),
                TextInsertion,
                NotificationService,
                _logger,
                tailDuration ?? TimeSpan.FromMilliseconds(50),
                SegmenterFactory,
                new SegmentedDecodeOptions
                {
                    // 一块音频即够一批，让测试无需喂大量数据。
                    MinDecodeBatch = TimeSpan.FromMilliseconds(200),
                    BoundaryPadding = TimeSpan.Zero
                });
        }

        public DictationCoordinator Coordinator { get; }

        public SettingsService Settings { get; }

        public FakeAudioCaptureService AudioCapture { get; }

        public FakeAsrEngine AsrEngine { get; }

        public FakeSegmenterFactory SegmenterFactory { get; }

        public FakeTextInsertionService TextInsertion { get; }

        public FakeNotificationService NotificationService { get; }

        public void SaveSettings(
            bool enableSegmented,
            RecognitionMode mode = RecognitionMode.NonStreaming) =>
            Settings.Save(new AppSettings
            {
                RecognitionMode = mode,
                EnableStreamingPreview = false,
                EnablePostProcessingRules = false,
                EnablePunctuation = true,
                EnableVadSegmentedDecoding = enableSegmented
            });

        public void Dispose()
        {
            AudioCapture.Dispose();
            AsrEngine.Dispose();
            _logger.Dispose();

            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class FakeSegmenterFactory : IAudioSegmenterFactory
    {
        public bool ReturnNull { get; set; }

        public bool ThrowOnAccept { get; set; }

        public bool KeepSpeechActive { get; set; }

        public int CreatedCount { get; private set; }

        public IAudioSegmenter? TryCreate(TimeSpan bufferCapacity)
        {
            if (ReturnNull)
            {
                return null;
            }

            CreatedCount++;
            return new FakeSegmenter
            {
                ThrowOnAccept = ThrowOnAccept,
                KeepSpeechActive = KeepSpeechActive
            };
        }
    }

    /// <summary>每块喂入的音频切成一个语音段，段偏移与真实数据严格对应。</summary>
    private sealed class FakeSegmenter : IAudioSegmenter
    {
        private readonly Queue<AudioSegment> _queue = new();
        private readonly object _sync = new();
        private long _fedSamples;

        public bool ThrowOnAccept { get; init; }

        public bool KeepSpeechActive { get; init; }

        public bool IsSpeechActive => KeepSpeechActive;

        public void AcceptChunk(float[] samples)
        {
            if (ThrowOnAccept)
            {
                throw new InvalidOperationException("分段器故障。");
            }

            lock (_sync)
            {
                _queue.Enqueue(new AudioSegment(_fedSamples, samples));
                _fedSamples += samples.Length;
            }
        }

        public bool TryDequeue(out AudioSegment segment)
        {
            lock (_sync)
            {
                if (_queue.Count == 0)
                {
                    segment = null!;
                    return false;
                }

                segment = _queue.Dequeue();
                return true;
            }
        }

        public void Flush()
        {
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 按音频长度区分调用来源：整段解码收到的是录音全长（3200 样本），
    /// 分段解码收到的是各批合并后的音频。
    /// </summary>
    private sealed class FakeAsrEngine : IAsrEngine
    {
        private const int WholeAudioSampleCount = 3200;
        private int _segmentIndex;

        public bool IsReady => true;

        public bool FailSegmentBatches { get; set; }

        public int WholeAudioCallCount { get; private set; }

        public Task InitializeAsync(CancellationToken ct = default, bool forceReprovision = false) =>
            Task.CompletedTask;

        public Task UnloadAsync() => Task.CompletedTask;

        public Task<AsrResult> TranscribeAsync(float[] pcm16kMono, CancellationToken ct = default)
        {
            if (pcm16kMono.Length == WholeAudioSampleCount)
            {
                WholeAudioCallCount++;
                return Task.FromResult(new AsrResult
                {
                    Success = true,
                    Text = "整段结果",
                    AudioDuration = TimeSpan.FromSeconds(pcm16kMono.Length / 16000d)
                });
            }

            if (FailSegmentBatches)
            {
                return Task.FromResult(new AsrResult
                {
                    Success = false,
                    Error = "解码失败"
                });
            }

            var index = Interlocked.Increment(ref _segmentIndex);
            return Task.FromResult(new AsrResult
            {
                Success = true,
                Text = index switch
                {
                    1 => "分段一",
                    2 => "分段二",
                    _ => $"分段{index}"
                },
                AudioDuration = TimeSpan.FromSeconds(pcm16kMono.Length / 16000d)
            });
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeAudioCaptureService : IAudioCaptureService
    {
        private static readonly float[] WholeAudio = Enumerable.Repeat(0.2f, 3200).ToArray();

        public int StopCallCount { get; private set; }

        public bool IsRecording { get; private set; }

        public event EventHandler<AudioChunkAvailableEventArgs>? AudioChunkAvailable;

        public event EventHandler<AudioCaptureStoppedEventArgs>? RecordingStopped;

        public IReadOnlyList<AudioDeviceInfo> GetInputDevices() => [];

        public Task StartAsync(string? preferredDeviceName, CancellationToken ct = default)
        {
            IsRecording = true;
            return Task.CompletedTask;
        }

        /// <summary>模拟录音回调推送一块音频。</summary>
        public void EmitChunk(int sampleCount)
        {
            var samples = new float[sampleCount];
            Array.Fill(samples, 0.2f);
            AudioChunkAvailable?.Invoke(this, new AudioChunkAvailableEventArgs(samples));
        }

        public Task<RecordedAudio> StopAsync(CancellationToken ct = default)
        {
            StopCallCount++;
            IsRecording = false;
            _ = RecordingStopped;
            return Task.FromResult(new RecordedAudio(WholeAudio, TimeSpan.FromMilliseconds(200)));
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeModelProvisioningService : IModelProvisioningService
    {
        public Task<ModelReadyResult> EnsureReadyAsync(
            AsrModelKind kind,
            bool downloadIfMissing,
            CancellationToken ct = default) =>
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

    private sealed class FakeStreamingAsrEngine : IStreamingAsrEngine
    {
        public bool IsReady => true;

        public Task InitializeAsync(CancellationToken ct = default, bool forceReprovision = false) =>
            Task.CompletedTask;

        public Task UnloadAsync() => Task.CompletedTask;

        public IStreamingAsrSession CreateSession() => new FakeStreamingAsrSession();

        public void Dispose()
        {
        }
    }

    private sealed class FakeStreamingAsrSession : IStreamingAsrSession
    {
        public ValueTask AcceptAudioAsync(float[] pcm16kMonoChunk, CancellationToken ct = default) =>
            ValueTask.CompletedTask;

        public StreamingAsrResult GetCurrentResult() => new();

        public ValueTask<StreamingAsrResult> CompleteAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(new StreamingAsrResult());

        public void Dispose()
        {
        }
    }

    private sealed class FakePunctuationService : IPunctuationService
    {
        public bool IsEnabled => true;

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
            new() { Input = input, Output = input };

        public PostProcessingTraceResult TestProcess(
            PostProcessingConfig config,
            string input,
            RuleExecutionContext context) =>
            new() { Input = input, Output = input };
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

        public Task<InsertionResult> InsertAsync(
            string text,
            ForegroundContext context,
            CancellationToken ct = default)
        {
            InsertedTexts.Add(text);
            InsertCalled.TrySetResult();
            return Task.FromResult(new InsertionResult { Success = true, Method = "test" });
        }
    }

    private sealed class FakeNotificationService : INotificationService
    {
        public Collection<string> WarnMessages { get; } = [];

        public void Info(string title, string message)
        {
        }

        public void Warn(string title, string message) => WarnMessages.Add(message);

        public void Error(string title, string message)
        {
        }
    }
}
