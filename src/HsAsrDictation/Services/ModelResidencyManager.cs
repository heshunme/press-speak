using HsAsrDictation.Asr;
using HsAsrDictation.Models;
using HsAsrDictation.Settings;

namespace HsAsrDictation.Services;

public sealed class ModelResidencyManager
{
    private readonly IModelProvisioningService _modelProvisioningService;
    private readonly IAsrEngine _asrEngine;
    private readonly IStreamingAsrEngine _streamingAsrEngine;

    public ModelResidencyManager(
        IModelProvisioningService modelProvisioningService,
        IAsrEngine asrEngine,
        IStreamingAsrEngine streamingAsrEngine)
    {
        _modelProvisioningService = modelProvisioningService;
        _asrEngine = asrEngine;
        _streamingAsrEngine = streamingAsrEngine;
    }

    public Task<ModelResidencyResult> EnsureModeReadyAsync(
        RecognitionMode mode,
        bool downloadIfMissing,
        bool reinitialize,
        bool allowUnload,
        CancellationToken ct = default) =>
        ReconcileAsync(
            mode,
            reinitialize,
            allowUnload,
            kind => _modelProvisioningService.EnsureReadyAsync(kind, downloadIfMissing, ct),
            ct);

    public Task<ModelResidencyResult> RedownloadModeAsync(
        RecognitionMode mode,
        bool reinitialize,
        bool allowUnload,
        CancellationToken ct = default) =>
        ReconcileAsync(
            mode,
            reinitialize,
            allowUnload,
            kind => _modelProvisioningService.DownloadAsync(kind, ct),
            ct);

    private async Task<ModelResidencyResult> ReconcileAsync(
        RecognitionMode mode,
        bool reinitialize,
        bool allowUnload,
        Func<AsrModelKind, Task<ModelReadyResult>> provisionAsync,
        CancellationToken ct)
    {
        switch (mode)
        {
            case RecognitionMode.NonStreaming:
            {
                var offlineReady = await EnsureOfflineAsync(provisionAsync, reinitialize, ct);
                if (!offlineReady.Success)
                {
                    return offlineReady;
                }

                if (allowUnload)
                {
                    _streamingAsrEngine.Unload();
                }

                return ModelResidencyResult.Ready();
            }
            case RecognitionMode.StreamingOnly:
            {
                var streamingReady = await EnsureStreamingAsync(provisionAsync, reinitialize, ct);
                if (!streamingReady.Success)
                {
                    return streamingReady;
                }

                if (allowUnload)
                {
                    _asrEngine.Unload();
                }

                return ModelResidencyResult.Ready();
            }
            case RecognitionMode.Hybrid:
            {
                var offlineReady = await EnsureOfflineAsync(provisionAsync, reinitialize, ct);
                if (!offlineReady.Success)
                {
                    return offlineReady;
                }

                var streamingReady = await EnsureStreamingAsync(provisionAsync, reinitialize, ct);
                if (!streamingReady.Success)
                {
                    return streamingReady;
                }

                return ModelResidencyResult.Ready();
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知识别模式。");
        }
    }

    private async Task<ModelResidencyResult> EnsureOfflineAsync(
        Func<AsrModelKind, Task<ModelReadyResult>> provisionAsync,
        bool reinitialize,
        CancellationToken ct)
    {
        var ready = await provisionAsync(AsrModelKind.Offline);
        if (!ready.IsReady)
        {
            return ModelResidencyResult.NotReady(ready.ErrorMessage ?? "离线模型未就绪。");
        }

        if (reinitialize || !_asrEngine.IsReady)
        {
            await _asrEngine.InitializeAsync(ct);
        }

        return ModelResidencyResult.Ready();
    }

    private async Task<ModelResidencyResult> EnsureStreamingAsync(
        Func<AsrModelKind, Task<ModelReadyResult>> provisionAsync,
        bool reinitialize,
        CancellationToken ct)
    {
        var ready = await provisionAsync(AsrModelKind.Streaming);
        if (!ready.IsReady)
        {
            return ModelResidencyResult.NotReady(ready.ErrorMessage ?? "流式模型未就绪。");
        }

        if (reinitialize || !_streamingAsrEngine.IsReady)
        {
            await _streamingAsrEngine.InitializeAsync(ct);
        }

        return ModelResidencyResult.Ready();
    }
}

public sealed class ModelResidencyResult
{
    public bool Success { get; init; }

    public string? ErrorMessage { get; init; }

    public static ModelResidencyResult Ready() => new()
    {
        Success = true
    };

    public static ModelResidencyResult NotReady(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}
