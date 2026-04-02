namespace HsAsrDictation.Models;

public interface IModelProvisioningService
{
    Task<ModelReadyResult> EnsureReadyAsync(bool downloadIfMissing, CancellationToken ct = default);

    Task<ModelReadyResult> DownloadAsync(CancellationToken ct = default);
}
