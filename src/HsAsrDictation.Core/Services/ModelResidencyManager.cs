using HsAsrDictation.Asr;
using HsAsrDictation.Models;
using HsAsrDictation.Settings;

namespace HsAsrDictation.Services;

public sealed class ModelResidencyManager
{
    private readonly IModelProvisioningService _modelProvisioningService;
    private readonly IAsrEngine _asrEngine;
    private readonly IStreamingAsrEngine _streamingAsrEngine;
    private readonly SemaphoreSlim _reconcileLock = new(1, 1);

    public ModelResidencyManager(
        IModelProvisioningService modelProvisioningService,
        IAsrEngine asrEngine,
        IStreamingAsrEngine streamingAsrEngine)
    {
        _modelProvisioningService = modelProvisioningService;
        _asrEngine = asrEngine;
        _streamingAsrEngine = streamingAsrEngine;
    }

    /// <param name="allowUnload">
    /// 卸载许可探针（通常是"当前是否空闲"）：不信任调用方传入时刻的快照，
    /// 在持有 <see cref="_reconcileLock"/>、真正卸载前实时求值——
    /// 等锁与 provisioning IO 期间用户可能已经开始录音。
    /// </param>
    public Task<ModelResidencyResult> EnsureModeReadyAsync(
        RecognitionMode mode,
        bool downloadIfMissing,
        bool reinitialize,
        Func<bool> allowUnload,
        CancellationToken ct = default) =>
        ReconcileAsync(
            mode,
            reinitialize,
            allowUnload,
            kind => _modelProvisioningService.EnsureReadyAsync(kind, downloadIfMissing, ct),
            ct);

    /// <param name="allowUnload">同 <see cref="EnsureModeReadyAsync"/>，卸载前实时求值。</param>
    public Task<ModelResidencyResult> RedownloadModeAsync(
        RecognitionMode mode,
        bool reinitialize,
        Func<bool> allowUnload,
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
        Func<bool> allowUnload,
        Func<AsrModelKind, Task<ModelReadyResult>> provisionAsync,
        CancellationToken ct)
    {
        await _reconcileLock.WaitAsync(ct);
        try
        {
            return await ReconcileCoreAsync(mode, reinitialize, allowUnload, provisionAsync, ct);
        }
        finally
        {
            _reconcileLock.Release();
        }
    }

    private async Task<ModelResidencyResult> ReconcileCoreAsync(
        RecognitionMode mode,
        bool reinitialize,
        Func<bool> allowUnload,
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

                if (allowUnload())
                {
                    await _streamingAsrEngine.UnloadAsync();
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

                if (allowUnload())
                {
                    await _asrEngine.UnloadAsync();
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
            // 显式重建必须强制 provisioning（例如重下载后目录路径未变），不能走指纹短路。
            await _asrEngine.InitializeAsync(ct, forceReprovision: reinitialize);
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
            // 显式重建必须强制 provisioning（例如重下载后目录路径未变），不能走指纹短路。
            await _streamingAsrEngine.InitializeAsync(ct, forceReprovision: reinitialize);
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
