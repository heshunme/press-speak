using HsAsrDictation.Asr;
using HsAsrDictation.Models;
using HsAsrDictation.Services;
using HsAsrDictation.Settings;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class ModelResidencyManagerTests
{
    [Fact]
    public async Task EnsureModeReadyAsync_NonStreaming_UnloadsStreamingEngine()
    {
        var provisioning = new FakeModelProvisioningService
        {
            EnsureResults =
            {
                [AsrModelKind.Offline] = Ready()
            }
        };
        var offlineEngine = new FakeAsrEngine();
        var streamingEngine = new FakeStreamingAsrEngine(isReady: true);
        var manager = new ModelResidencyManager(provisioning, offlineEngine, streamingEngine);

        var result = await manager.EnsureModeReadyAsync(
            RecognitionMode.NonStreaming,
            downloadIfMissing: false,
            reinitialize: false,
            allowUnload: true);

        Assert.True(result.Success);
        Assert.Equal([AsrModelKind.Offline], provisioning.EnsureRequests);
        Assert.Equal(1, offlineEngine.InitializeCallCount);
        Assert.Equal(1, streamingEngine.UnloadCallCount);
        Assert.False(streamingEngine.IsReady);
    }

    [Fact]
    public async Task EnsureModeReadyAsync_StreamingOnly_UnloadsOfflineEngine()
    {
        var provisioning = new FakeModelProvisioningService
        {
            EnsureResults =
            {
                [AsrModelKind.Streaming] = Ready()
            }
        };
        var offlineEngine = new FakeAsrEngine(isReady: true);
        var streamingEngine = new FakeStreamingAsrEngine();
        var manager = new ModelResidencyManager(provisioning, offlineEngine, streamingEngine);

        var result = await manager.EnsureModeReadyAsync(
            RecognitionMode.StreamingOnly,
            downloadIfMissing: false,
            reinitialize: false,
            allowUnload: true);

        Assert.True(result.Success);
        Assert.Equal([AsrModelKind.Streaming], provisioning.EnsureRequests);
        Assert.Equal(1, streamingEngine.InitializeCallCount);
        Assert.Equal(1, offlineEngine.UnloadCallCount);
        Assert.False(offlineEngine.IsReady);
    }

    [Fact]
    public async Task EnsureModeReadyAsync_Hybrid_KeepsBothEnginesLoaded()
    {
        var provisioning = new FakeModelProvisioningService
        {
            EnsureResults =
            {
                [AsrModelKind.Offline] = Ready(),
                [AsrModelKind.Streaming] = Ready()
            }
        };
        var offlineEngine = new FakeAsrEngine();
        var streamingEngine = new FakeStreamingAsrEngine();
        var manager = new ModelResidencyManager(provisioning, offlineEngine, streamingEngine);

        var result = await manager.EnsureModeReadyAsync(
            RecognitionMode.Hybrid,
            downloadIfMissing: false,
            reinitialize: false,
            allowUnload: true);

        Assert.True(result.Success);
        Assert.Equal([AsrModelKind.Offline, AsrModelKind.Streaming], provisioning.EnsureRequests);
        Assert.Equal(1, offlineEngine.InitializeCallCount);
        Assert.Equal(1, streamingEngine.InitializeCallCount);
        Assert.Equal(0, offlineEngine.UnloadCallCount);
        Assert.Equal(0, streamingEngine.UnloadCallCount);
    }

    [Fact]
    public async Task EnsureModeReadyAsync_WhenRequiredModelNotReady_DoesNotUnloadActiveEnginePrematurely()
    {
        var provisioning = new FakeModelProvisioningService
        {
            EnsureResults =
            {
                [AsrModelKind.Offline] = NotReady("offline missing")
            }
        };
        var offlineEngine = new FakeAsrEngine();
        var streamingEngine = new FakeStreamingAsrEngine(isReady: true);
        var manager = new ModelResidencyManager(provisioning, offlineEngine, streamingEngine);

        var result = await manager.EnsureModeReadyAsync(
            RecognitionMode.NonStreaming,
            downloadIfMissing: false,
            reinitialize: false,
            allowUnload: true);

        Assert.False(result.Success);
        Assert.Equal("offline missing", result.ErrorMessage);
        Assert.Equal(0, offlineEngine.InitializeCallCount);
        Assert.Equal(0, streamingEngine.UnloadCallCount);
        Assert.True(streamingEngine.IsReady);
    }

    [Fact]
    public async Task RedownloadModeAsync_NonStreaming_ReinitializesOfflineAndUnloadsStreaming()
    {
        var provisioning = new FakeModelProvisioningService
        {
            DownloadResults =
            {
                [AsrModelKind.Offline] = Ready()
            }
        };
        var offlineEngine = new FakeAsrEngine(isReady: true);
        var streamingEngine = new FakeStreamingAsrEngine(isReady: true);
        var manager = new ModelResidencyManager(provisioning, offlineEngine, streamingEngine);

        var result = await manager.RedownloadModeAsync(
            RecognitionMode.NonStreaming,
            reinitialize: true,
            allowUnload: true);

        Assert.True(result.Success);
        Assert.Equal([AsrModelKind.Offline], provisioning.DownloadRequests);
        Assert.Equal(1, offlineEngine.InitializeCallCount);
        Assert.Equal(1, streamingEngine.UnloadCallCount);
        Assert.False(streamingEngine.IsReady);
    }

    private static ModelReadyResult Ready() => new()
    {
        IsReady = true,
        ModelDirectory = "/tmp/model"
    };

    private static ModelReadyResult NotReady(string errorMessage) => new()
    {
        IsReady = false,
        ErrorMessage = errorMessage
    };

    private sealed class FakeModelProvisioningService : IModelProvisioningService
    {
        public Dictionary<AsrModelKind, ModelReadyResult> EnsureResults { get; } = [];

        public Dictionary<AsrModelKind, ModelReadyResult> DownloadResults { get; } = [];

        public List<AsrModelKind> EnsureRequests { get; } = [];

        public List<AsrModelKind> DownloadRequests { get; } = [];

        public Task<ModelReadyResult> EnsureReadyAsync(
            AsrModelKind kind,
            bool downloadIfMissing,
            CancellationToken ct = default)
        {
            EnsureRequests.Add(kind);
            return Task.FromResult(EnsureResults.TryGetValue(kind, out var result) ? result : NotReady($"{kind} missing"));
        }

        public Task<ModelReadyResult> DownloadAsync(AsrModelKind kind, CancellationToken ct = default)
        {
            DownloadRequests.Add(kind);
            return Task.FromResult(DownloadResults.TryGetValue(kind, out var result) ? result : NotReady($"{kind} missing"));
        }
    }

    private sealed class FakeAsrEngine : IAsrEngine
    {
        public FakeAsrEngine(bool isReady = false)
        {
            IsReady = isReady;
        }

        public bool IsReady { get; private set; }

        public int InitializeCallCount { get; private set; }

        public int UnloadCallCount { get; private set; }

        public Task InitializeAsync(CancellationToken ct = default)
        {
            InitializeCallCount++;
            IsReady = true;
            return Task.CompletedTask;
        }

        public void Unload()
        {
            UnloadCallCount++;
            IsReady = false;
        }

        public Task<AsrResult> TranscribeAsync(float[] pcm16kMono, CancellationToken ct = default) =>
            Task.FromResult(new AsrResult());

        public void Dispose()
        {
        }
    }

    private sealed class FakeStreamingAsrEngine : IStreamingAsrEngine
    {
        public FakeStreamingAsrEngine(bool isReady = false)
        {
            IsReady = isReady;
        }

        public bool IsReady { get; private set; }

        public int InitializeCallCount { get; private set; }

        public int UnloadCallCount { get; private set; }

        public Task InitializeAsync(CancellationToken ct = default)
        {
            InitializeCallCount++;
            IsReady = true;
            return Task.CompletedTask;
        }

        public void Unload()
        {
            UnloadCallCount++;
            IsReady = false;
        }

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
}
