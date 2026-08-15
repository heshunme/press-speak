using System.Net.Http;
using System.IO;
using HsAsrDictation.Logging;
using HsAsrDictation.Settings;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace HsAsrDictation.Models;

public sealed class ModelProvisioningService : IModelProvisioningService
{
    // 下载正文的总超时兜底：HttpClient.Timeout 只覆盖到响应头（ResponseHeadersRead），
    // 正文流只靠调用方 ct，现有调用方全传 default，网络挂起时会永久卡住。
    private static readonly TimeSpan DefaultDownloadBodyTimeout = TimeSpan.FromMinutes(10);

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _downloadBodyTimeout;
    private readonly SettingsService _settingsService;
    private readonly LocalLogService _logger;

    public ModelProvisioningService(
        SettingsService settingsService,
        LocalLogService logger,
        HttpMessageHandler? httpMessageHandler = null,
        TimeSpan? downloadBodyTimeout = null)
    {
        _settingsService = settingsService;
        _logger = logger;
        _httpClient = httpMessageHandler is null ? new HttpClient() : new HttpClient(httpMessageHandler);
        _downloadBodyTimeout = downloadBodyTimeout ?? DefaultDownloadBodyTimeout;
    }

    public async Task<ModelReadyResult> EnsureReadyAsync(AsrModelKind kind, bool downloadIfMissing, CancellationToken ct = default)
    {
        var current = _settingsService.Current;
        var modelRootPath = GetModelRootPath(current, kind);
        Directory.CreateDirectory(modelRootPath);

        var existing = ValidateExistingModel(kind, modelRootPath);
        if (existing.IsReady)
        {
            _logger.Info($"复用现有模型目录：{existing.ModelDirectory}");
            return existing;
        }

        if (!downloadIfMissing)
        {
            _logger.Warn($"模型未就绪，且当前配置禁止自动下载：{modelRootPath}");
            return existing;
        }

        return await DownloadAsync(kind, ct);
    }

    public async Task<ModelReadyResult> DownloadAsync(AsrModelKind kind, CancellationToken ct = default)
    {
        var current = _settingsService.Current;
        var modelRootPath = GetModelRootPath(current, kind);
        var definition = ModelManifest.GetDefinition(kind);
        Directory.CreateDirectory(modelRootPath);

        var archivePath = Path.Combine(modelRootPath, $"{definition.ExtractedDirectoryName}.tar.bz2");
        var extractionTarget = modelRootPath;

        _logger.Info($"开始下载模型：{definition.ArchiveUrl}");

        // 下载或解压失败时清理半成品压缩包。
        try
        {
            using (var response = await _httpClient.GetAsync(
                       definition.ArchiveUrl,
                       HttpCompletionOption.ResponseHeadersRead,
                       ct))
            {
                response.EnsureSuccessStatusCode();

                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(archivePath);

                // 正文传输用独立超时令牌兜底，防止网络挂起后永久占用上层锁。
                using var bodyTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                bodyTimeoutCts.CancelAfter(_downloadBodyTimeout);
                try
                {
                    await input.CopyToAsync(output, bodyTimeoutCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // 由下载超时触发，与调用方主动取消区分开。
                    throw new TimeoutException($"模型下载超时（{_downloadBodyTimeout}）：{definition.ArchiveUrl}");
                }
            }

            _logger.Info("模型下载完成，开始解压。");

            // 解压是纯同步 CPU+IO，放线程池执行，避免阻塞调用方（UI）线程。
            await Task.Run(() =>
            {
                using var archiveStream = File.OpenRead(archivePath);
                using var reader = ReaderFactory.Open(archiveStream);
                while (reader.MoveToNextEntry())
                {
                    ct.ThrowIfCancellationRequested();

                    if (reader.Entry.IsDirectory)
                    {
                        continue;
                    }

                    reader.WriteEntryToDirectory(extractionTarget, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true,
                        PreserveFileTime = true
                    });
                }
            }, ct);
        }
        finally
        {
            TryDeleteFile(archivePath);
        }

        _logger.Info("模型解压完成。");
        return ValidateExistingModel(kind, modelRootPath);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string GetModelRootPath(AppSettings settings, AsrModelKind kind) =>
        kind switch
        {
            AsrModelKind.Offline => settings.OfflineModelRootPath,
            AsrModelKind.Streaming => settings.StreamingModelRootPath,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知模型类型。")
        };

    private ModelReadyResult ValidateExistingModel(AsrModelKind kind, string modelRootPath)
    {
        var definition = ModelManifest.GetDefinition(kind);

        foreach (var candidate in EnumerateCandidateDirectories(modelRootPath))
        {
            var missingEntries = definition.RequiredRelativePaths
                .Where(path => !Directory.Exists(Path.Combine(candidate, path)) &&
                               !File.Exists(Path.Combine(candidate, path)))
                .ToArray();

            if (missingEntries.Length == 0)
            {
                return new ModelReadyResult
                {
                    IsReady = true,
                    ModelDirectory = candidate
                };
            }
        }

        return new ModelReadyResult
        {
            IsReady = false,
            MissingEntries = definition.RequiredRelativePaths,
            ErrorMessage = $"未找到完整模型，期望目录：{modelRootPath}"
        };
    }

    private static IEnumerable<string> EnumerateCandidateDirectories(string modelRootPath)
    {
        yield return modelRootPath;

        if (!Directory.Exists(modelRootPath))
        {
            yield break;
        }

        foreach (string child in Directory.EnumerateDirectories(modelRootPath))
        {
            yield return child;
        }
    }
}
