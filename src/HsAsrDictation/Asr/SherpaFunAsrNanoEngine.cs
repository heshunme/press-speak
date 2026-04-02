using System.IO;
using System.Diagnostics;
using HsAsrDictation.Logging;
using HsAsrDictation.Models;
using HsAsrDictation.Settings;
using SherpaOnnx;

namespace HsAsrDictation.Asr;

public sealed class SherpaFunAsrNanoEngine : IAsrEngine
{
    private readonly IModelProvisioningService _modelProvisioningService;
    private readonly SettingsService _settingsService;
    private readonly LocalLogService _logger;
    private readonly SemaphoreSlim _decodeLock = new(1, 1);
    private OfflineRecognizer? _recognizer;
    private string? _activeModelDirectory;

    public SherpaFunAsrNanoEngine(
        IModelProvisioningService modelProvisioningService,
        SettingsService settingsService,
        LocalLogService logger)
    {
        _modelProvisioningService = modelProvisioningService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public bool IsReady => _recognizer is not null;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var ready = await _modelProvisioningService.EnsureReadyAsync(
            AsrModelKind.Offline,
            _settingsService.Current.AutoDownloadModel,
            ct);

        if (!ready.IsReady || string.IsNullOrWhiteSpace(ready.ModelDirectory))
        {
            throw new InvalidOperationException(ready.ErrorMessage ?? "模型不可用。");
        }

        if (_recognizer is not null &&
            string.Equals(_activeModelDirectory, ready.ModelDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _recognizer?.Dispose();

        var config = new OfflineRecognizerConfig();
        config.ModelConfig.FunAsrNano.EncoderAdaptor = Path.Combine(ready.ModelDirectory, "encoder_adaptor.int8.onnx");
        config.ModelConfig.FunAsrNano.LLM = Path.Combine(ready.ModelDirectory, "llm.int8.onnx");
        config.ModelConfig.FunAsrNano.Embedding = Path.Combine(ready.ModelDirectory, "embedding.int8.onnx");
        config.ModelConfig.FunAsrNano.Tokenizer = Path.Combine(ready.ModelDirectory, "Qwen3-0.6B");
        config.ModelConfig.Tokens = string.Empty;
        config.ModelConfig.Debug = 0;

        _recognizer = await Task.Run(() => new OfflineRecognizer(config), ct);
        _activeModelDirectory = ready.ModelDirectory;
        _logger.Info($"ASR 引擎已初始化：{ready.ModelDirectory}");
    }

    public async Task<AsrResult> TranscribeAsync(float[] pcm16kMono, CancellationToken ct = default)
    {
        if (pcm16kMono.Length == 0)
        {
            return new AsrResult
            {
                Success = false,
                Error = "音频为空。"
            };
        }

        await InitializeAsync(ct);

        if (_recognizer is null)
        {
            return new AsrResult
            {
                Success = false,
                Error = "识别器未初始化。"
            };
        }

        await _decodeLock.WaitAsync(ct);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var text = await Task.Run(() =>
            {
                using var stream = _recognizer.CreateStream();
                stream.AcceptWaveform(16000, pcm16kMono);
                _recognizer.Decode(stream);
                return stream.Result.Text?.Trim() ?? string.Empty;
            }, ct);

            stopwatch.Stop();

            return new AsrResult
            {
                Success = !string.IsNullOrWhiteSpace(text),
                Text = text,
                AudioDuration = TimeSpan.FromSeconds(pcm16kMono.Length / 16000d),
                DecodeLatency = stopwatch.Elapsed,
                Error = string.IsNullOrWhiteSpace(text) ? "模型未返回文本。" : null
            };
        }
        catch (Exception ex)
        {
            _logger.Error("ASR 解码失败。", ex);
            return new AsrResult
            {
                Success = false,
                Error = ex.Message,
                AudioDuration = TimeSpan.FromSeconds(pcm16kMono.Length / 16000d)
            };
        }
        finally
        {
            _decodeLock.Release();
        }
    }

    public void Dispose()
    {
        Unload();
        _decodeLock.Dispose();
    }

    public void Unload()
    {
        _recognizer?.Dispose();
        _recognizer = null;
        _activeModelDirectory = null;
    }
}
