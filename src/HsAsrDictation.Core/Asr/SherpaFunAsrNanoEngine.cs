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
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private OfflineRecognizer? _recognizer;
    private string? _activeModelDirectory;
    private string? _activeHotwords;
    private EngineInitFingerprint? _activeFingerprint;

    public SherpaFunAsrNanoEngine(
        IModelProvisioningService modelProvisioningService,
        SettingsService settingsService,
        LocalLogService logger)
    {
        _modelProvisioningService = modelProvisioningService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public bool IsReady => Volatile.Read(ref _recognizer) is not null;

    public async Task InitializeAsync(CancellationToken ct = default, bool forceReprovision = false)
    {
        // _initLock 序列化并发初始化（启动预热、设置保存 reinit、Transcribe 前置检查可能重叠），
        // 避免重复构建识别器导致落败一方的原生内存泄漏。
        await _initLock.WaitAsync(ct);
        try
        {
            var fingerprint = EngineInitFingerprint.Capture(_settingsService.Current, AsrModelKind.Offline);

            // 已按当前设置初始化时短路：每次听写都会经 TranscribeAsync 走到这里，
            // 跳过重复的目录校验与日志；显式重建（设置保存/重下载）用 forceReprovision 强制。
            if (!forceReprovision &&
                Volatile.Read(ref _recognizer) is not null &&
                _activeFingerprint?.Matches(fingerprint) == true)
            {
                return;
            }

            var ready = await _modelProvisioningService.EnsureReadyAsync(
                AsrModelKind.Offline,
                _settingsService.Current.AutoDownloadModel,
                ct);

            if (!ready.IsReady || string.IsNullOrWhiteSpace(ready.ModelDirectory))
            {
                throw new InvalidOperationException(ready.ErrorMessage ?? "模型不可用。");
            }

            // 热词经 LLM prompt 注入，属于识别器构造期配置：内容变化必须重建识别器，
            // 因此复用判断使用"模型目录 + 热词"组合指纹。
            var hotwords = HotwordsNormalizer.NormalizeToStorage(_settingsService.Current.Hotwords);

            if (_recognizer is not null &&
                string.Equals(_activeModelDirectory, ready.ModelDirectory, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_activeHotwords, hotwords, StringComparison.Ordinal))
            {
                return;
            }

            var config = new OfflineRecognizerConfig();
            config.ModelConfig.FunAsrNano.EncoderAdaptor = Path.Combine(ready.ModelDirectory, "encoder_adaptor.int8.onnx");
            config.ModelConfig.FunAsrNano.LLM = Path.Combine(ready.ModelDirectory, "llm.int8.onnx");
            config.ModelConfig.FunAsrNano.Embedding = Path.Combine(ready.ModelDirectory, "embedding.int8.onnx");
            config.ModelConfig.FunAsrNano.Tokenizer = Path.Combine(ready.ModelDirectory, "Qwen3-0.6B");
            config.ModelConfig.FunAsrNano.Hotwords = hotwords;
            // C# 绑定默认 Itn=0（与上游 Python 默认 itn=True 不一致），且 Itn=0 会让 prompt 追加
            // "不进行文本规整"。听写场景期望数字/日期按书面形式输出，这里显式启用 ITN。
            config.ModelConfig.FunAsrNano.Itn = 1;
            config.ModelConfig.Tokens = string.Empty;
            config.ModelConfig.Debug = 0;

            var recognizer = await Task.Run(() => new OfflineRecognizer(config), ct);

            // 替换（含释放旧识别器）在解码锁内进行，与进行中的 Transcribe 互斥。
            await _decodeLock.WaitAsync(CancellationToken.None);
            try
            {
                _recognizer?.Dispose();
                _recognizer = recognizer;
                _activeModelDirectory = ready.ModelDirectory;
                _activeHotwords = hotwords;
                _activeFingerprint = fingerprint;
            }
            finally
            {
                _decodeLock.Release();
            }

            _logger.Info($"ASR 引擎已初始化：{ready.ModelDirectory}");
        }
        finally
        {
            _initLock.Release();
        }
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

        await _decodeLock.WaitAsync(ct);
        try
        {
            var recognizer = _recognizer;
            if (recognizer is null)
            {
                return new AsrResult
                {
                    Success = false,
                    Error = "识别器未初始化。"
                };
            }

            var stopwatch = Stopwatch.StartNew();
            var text = await Task.Run(() =>
            {
                using var stream = recognizer.CreateStream();
                stream.AcceptWaveform(16000, pcm16kMono);
                recognizer.Decode(stream);
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
        catch (OperationCanceledException)
        {
            // 取消是正常控制流，不应记为解码失败。
            throw;
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
        UnloadAsync().GetAwaiter().GetResult();
        _decodeLock.Dispose();
        _initLock.Dispose();
    }

    public async Task UnloadAsync()
    {
        // 异步等待解码锁：与进行中的 Transcribe 互斥，但不阻塞调用线程（UI continuation）。
        await _decodeLock.WaitAsync(CancellationToken.None);
        try
        {
            _recognizer?.Dispose();
            _recognizer = null;
            _activeModelDirectory = null;
            _activeHotwords = null;
            _activeFingerprint = null;
        }
        finally
        {
            _decodeLock.Release();
        }
    }
}
