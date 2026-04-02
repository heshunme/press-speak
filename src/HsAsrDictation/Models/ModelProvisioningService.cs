using System.Net.Http;
using System.IO;
using HsAsrDictation.Logging;
using HsAsrDictation.Settings;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace HsAsrDictation.Models;

public sealed class ModelProvisioningService : IModelProvisioningService
{
    private static readonly HttpClient HttpClient = new();
    private readonly SettingsService _settingsService;
    private readonly LocalLogService _logger;

    public ModelProvisioningService(SettingsService settingsService, LocalLogService logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<ModelReadyResult> EnsureReadyAsync(bool downloadIfMissing, CancellationToken ct = default)
    {
        var current = _settingsService.Current;
        Directory.CreateDirectory(current.ModelRootPath);

        var existing = ValidateExistingModel(current.ModelRootPath);
        if (existing.IsReady)
        {
            _logger.Info($"复用现有模型目录：{existing.ModelDirectory}");
            return existing;
        }

        if (!downloadIfMissing)
        {
            _logger.Warn($"模型未就绪，且当前配置禁止自动下载：{current.ModelRootPath}");
            return existing;
        }

        return await DownloadAsync(ct);
    }

    public async Task<ModelReadyResult> DownloadAsync(CancellationToken ct = default)
    {
        var current = _settingsService.Current;
        Directory.CreateDirectory(current.ModelRootPath);

        var archivePath = Path.Combine(current.ModelRootPath, $"{ModelManifest.ExtractedDirectoryName}.tar.bz2");
        var extractionTarget = current.ModelRootPath;

        _logger.Info($"开始下载模型：{ModelManifest.ArchiveUrl}");

        using (var response = await HttpClient.GetAsync(
                   ModelManifest.ArchiveUrl,
                   HttpCompletionOption.ResponseHeadersRead,
                   ct))
        {
            response.EnsureSuccessStatusCode();

            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(archivePath);
            await input.CopyToAsync(output, ct);
        }

        _logger.Info("模型下载完成，开始解压。");

        using (var archiveStream = File.OpenRead(archivePath))
        using (var reader = ReaderFactory.Open(archiveStream))
        {
            while (reader.MoveToNextEntry())
            {
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
        }

        File.Delete(archivePath);
        _logger.Info("模型解压完成。");
        return ValidateExistingModel(current.ModelRootPath);
    }

    private ModelReadyResult ValidateExistingModel(string modelRootPath)
    {
        foreach (var candidate in EnumerateCandidateDirectories(modelRootPath))
        {
            var missingEntries = ModelManifest.RequiredRelativePaths
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
            MissingEntries = ModelManifest.RequiredRelativePaths,
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
