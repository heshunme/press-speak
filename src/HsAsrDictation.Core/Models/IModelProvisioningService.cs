namespace HsAsrDictation.Models;

public interface IModelProvisioningService
{
    Task<ModelReadyResult> EnsureReadyAsync(AsrModelKind kind, bool downloadIfMissing, CancellationToken ct = default);

    Task<ModelReadyResult> DownloadAsync(AsrModelKind kind, CancellationToken ct = default);
}
