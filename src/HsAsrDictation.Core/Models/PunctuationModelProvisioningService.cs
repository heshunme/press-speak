using System.IO;
using System.Net.Http;
using HsAsrDictation.Logging;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace HsAsrDictation.Models;

public sealed class PunctuationModelProvisioningService : IPunctuationModelProvisioningService
{
    // 下载正文的总超时兜底：HttpClient.Timeout 只覆盖到响应头（ResponseHeadersRead），
    // 正文流只靠调用方 ct，现有调用方全传 default，网络挂起时会永久卡住。
    private static readonly TimeSpan DefaultDownloadBodyTimeout = TimeSpan.FromMinutes(10);

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _downloadBodyTimeout;
    private readonly string _modelRootPath;
    private readonly LocalLogService _logger;

    public PunctuationModelProvisioningService(
        LocalLogService logger,
        string? modelRootPath = null,
        HttpMessageHandler? httpMessageHandler = null,
        TimeSpan? downloadBodyTimeout = null)
    {
        _logger = logger;
        _modelRootPath = string.IsNullOrWhiteSpace(modelRootPath)
            ? PunctuationModelManifest.GetModelRootPath()
            : modelRootPath;
        _httpClient = httpMessageHandler is null ? new HttpClient() : new HttpClient(httpMessageHandler);
        _downloadBodyTimeout = downloadBodyTimeout ?? DefaultDownloadBodyTimeout;
    }

    public async Task<ModelReadyResult> EnsureReadyAsync(bool downloadIfMissing, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_modelRootPath);

        var existing = ValidateExistingModel();
        if (existing.IsReady)
        {
            _logger.Info($"复用现有标点模型目录：{existing.ModelDirectory}");
            return existing;
        }

        if (!downloadIfMissing)
        {
            _logger.Warn($"标点模型未就绪，且当前配置禁止自动下载：{_modelRootPath}");
            return existing;
        }

        return await DownloadAsync(ct);
    }

    public async Task<ModelReadyResult> DownloadAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_modelRootPath);

        var archivePath = Path.Combine(_modelRootPath, $"{PunctuationModelManifest.ExtractedDirectoryName}.tar.bz2");
        var extractionTarget = Path.Combine(
            Path.GetTempPath(),
            "HsAsrDictation",
            "punctuation",
            Guid.NewGuid().ToString("N"));

        try
        {
            _logger.Info($"开始下载标点模型：{PunctuationModelManifest.ArchiveUrl}");
            Directory.CreateDirectory(extractionTarget);

            using (var response = await _httpClient.GetAsync(
                       PunctuationModelManifest.ArchiveUrl,
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
                    throw new TimeoutException($"标点模型下载超时（{_downloadBodyTimeout}）：{PunctuationModelManifest.ArchiveUrl}");
                }
            }

            _logger.Info("标点模型下载完成，开始解压。");

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

            var modelFile = Directory.EnumerateFiles(
                    extractionTarget,
                    PunctuationModelManifest.RequiredFileName,
                    SearchOption.AllDirectories)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(modelFile))
            {
                return new ModelReadyResult
                {
                    IsReady = false,
                    MissingEntries = PunctuationModelManifest.RequiredRelativePaths,
                    ErrorMessage = "标点模型解压后未找到 model.int8.onnx。"
                };
            }

            var targetDirectory = PunctuationModelManifest.GetModelDirectory(_modelRootPath);
            Directory.CreateDirectory(targetDirectory);
            File.Copy(modelFile, Path.Combine(targetDirectory, PunctuationModelManifest.RequiredFileName), overwrite: true);
            _logger.Info("标点模型解压完成。");
            return ValidateExistingModel();
        }
        finally
        {
            TryDeleteFile(archivePath);
            TryDeleteDirectory(extractionTarget);
        }
    }

    private ModelReadyResult ValidateExistingModel()
    {
        foreach (var candidate in EnumerateCandidateDirectories(_modelRootPath))
        {
            var modelFile = Path.Combine(candidate, PunctuationModelManifest.RequiredFileName);
            if (File.Exists(modelFile))
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
            MissingEntries = PunctuationModelManifest.RequiredRelativePaths,
            ErrorMessage = $"未找到完整标点模型，期望目录：{_modelRootPath}"
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
