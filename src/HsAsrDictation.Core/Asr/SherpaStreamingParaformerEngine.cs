using System.Diagnostics;
using System.IO;
using HsAsrDictation.Logging;
using HsAsrDictation.Models;
using HsAsrDictation.Settings;
using SherpaOnnx;

namespace HsAsrDictation.Asr;

public sealed class SherpaStreamingParaformerEngine : IStreamingAsrEngine
{
    // 等待活动会话归零的上限：超时后放弃本次等待，由最后一个会话的 Dispose 兜底释放识别器，
    // 宁可延迟释放也绝不 dispose 仍在解码的识别器（原生层崩溃会直接拖垮进程）。
    private static readonly TimeSpan SessionDrainTimeout = TimeSpan.FromSeconds(30);

    private readonly IModelProvisioningService _modelProvisioningService;
    private readonly SettingsService _settingsService;
    private readonly LocalLogService _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly object _recognizerSwapLock = new();
    private RefCountedResource<OnlineRecognizer>? _recognizerHolder;
    private string? _activeModelDirectory;
    private EngineInitFingerprint? _activeFingerprint;

    public SherpaStreamingParaformerEngine(
        IModelProvisioningService modelProvisioningService,
        SettingsService settingsService,
        LocalLogService logger)
    {
        _modelProvisioningService = modelProvisioningService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public bool IsReady => Volatile.Read(ref _recognizerHolder) is not null;

    public async Task InitializeAsync(CancellationToken ct = default, bool forceReprovision = false)
    {
        // _initLock 序列化并发初始化（启动预热、设置保存 reinit、会话启动可能重叠），
        // 避免重复构建识别器或对同一实例双重释放。
        await _initLock.WaitAsync(ct);
        try
        {
            var fingerprint = EngineInitFingerprint.Capture(_settingsService.Current, AsrModelKind.Streaming);

            // 已按当前设置初始化时短路：每次开始录音都会走到这里，
            // 跳过重复的目录校验与日志；显式重建（设置保存/重下载）用 forceReprovision 强制。
            if (!forceReprovision && IsCurrentFingerprint(fingerprint))
            {
                return;
            }

            var ready = await _modelProvisioningService.EnsureReadyAsync(
                AsrModelKind.Streaming,
                _settingsService.Current.AutoDownloadModel,
                ct);

            if (!ready.IsReady || string.IsNullOrWhiteSpace(ready.ModelDirectory))
            {
                throw new InvalidOperationException(ready.ErrorMessage ?? "流式模型不可用。");
            }

            if (IsLoadedFrom(ready.ModelDirectory))
            {
                return;
            }

            var config = new OnlineRecognizerConfig();
            config.FeatConfig.SampleRate = 16000;
            config.FeatConfig.FeatureDim = 80;
            config.ModelConfig.Paraformer.Encoder = Path.Combine(ready.ModelDirectory, "encoder.int8.onnx");
            config.ModelConfig.Paraformer.Decoder = Path.Combine(ready.ModelDirectory, "decoder.int8.onnx");
            config.ModelConfig.Tokens = Path.Combine(ready.ModelDirectory, "tokens.txt");
            config.ModelConfig.NumThreads = 1;
            config.ModelConfig.Provider = "cpu";
            config.DecodingMethod = "greedy_search";
            config.EnableEndpoint = 0;

            var recognizer = await Task.Run(() => new OnlineRecognizer(config), ct);

            RefCountedResource<OnlineRecognizer>? previous;
            lock (_recognizerSwapLock)
            {
                previous = _recognizerHolder;
                _recognizerHolder = new RefCountedResource<OnlineRecognizer>(recognizer);
                _activeModelDirectory = ready.ModelDirectory;
                _activeFingerprint = fingerprint;
            }

            // 旧识别器可能仍被活动流式会话持有：等会话归零（带超时）再释放，超时由最后的会话兜底释放。
            await RetireAndDisposeWhenDrainedAsync(previous);
            _logger.Info($"流式 ASR 引擎已初始化：{ready.ModelDirectory}");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public IStreamingAsrSession CreateSession()
    {
        lock (_recognizerSwapLock)
        {
            if (_recognizerHolder is not { } holder)
            {
                throw new InvalidOperationException("流式识别器未初始化。");
            }

            // 会话裸持有识别器引用，必须先登记租约，防止 Unload/替换在使用中释放。
            holder.AcquireLease();
            try
            {
                return new SherpaStreamingParaformerSession(holder.Resource, holder.ReleaseLease);
            }
            catch
            {
                holder.ReleaseLease();
                throw;
            }
        }
    }

    public void Dispose()
    {
        UnloadAsync().GetAwaiter().GetResult();
        _initLock.Dispose();
    }

    public async Task UnloadAsync()
    {
        RefCountedResource<OnlineRecognizer>? previous;
        lock (_recognizerSwapLock)
        {
            previous = _recognizerHolder;
            _recognizerHolder = null;
            _activeModelDirectory = null;
            _activeFingerprint = null;
        }

        await RetireAndDisposeWhenDrainedAsync(previous);
    }

    // 持有者与指纹/目录在 _recognizerSwapLock 内成对读写，避免与 Unload/替换并发时读到不一致的组合。
    private bool IsCurrentFingerprint(EngineInitFingerprint fingerprint)
    {
        lock (_recognizerSwapLock)
        {
            return _recognizerHolder is not null &&
                _activeFingerprint?.Matches(fingerprint) == true;
        }
    }

    private bool IsLoadedFrom(string modelDirectory)
    {
        lock (_recognizerSwapLock)
        {
            return _recognizerHolder is not null &&
                string.Equals(_activeModelDirectory, modelDirectory, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task RetireAndDisposeWhenDrainedAsync(RefCountedResource<OnlineRecognizer>? holder)
    {
        if (holder is null)
        {
            return;
        }

        if (await holder.RetireAndWaitForDrainAsync(SessionDrainTimeout))
        {
            holder.TryDispose();
        }
        else
        {
            _logger.Warn("等待流式会话结束超时，识别器将延迟到最后一个会话释放后再卸载。");
        }
    }

    private sealed class SherpaStreamingParaformerSession : IStreamingAsrSession
    {
        private readonly OnlineRecognizer _recognizer;
        private readonly OnlineStream _stream;
        private readonly Action _releaseLease;
        private readonly object _syncRoot = new();
        private StreamingAsrResult _currentResult = new();
        private int _totalSamples;
        private bool _isCompleted;
        private bool _disposed;

        public SherpaStreamingParaformerSession(OnlineRecognizer recognizer, Action releaseLease)
        {
            _recognizer = recognizer;
            _releaseLease = releaseLease;
            _stream = recognizer.CreateStream();
        }

        public ValueTask AcceptAudioAsync(float[] pcm16kMonoChunk, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (pcm16kMonoChunk.Length == 0)
            {
                return ValueTask.CompletedTask;
            }

            lock (_syncRoot)
            {
                ThrowIfDisposed();

                if (_isCompleted)
                {
                    throw new InvalidOperationException("流式会话已结束。");
                }

                var stopwatch = Stopwatch.StartNew();
                _stream.AcceptWaveform(16000, pcm16kMonoChunk);
                _totalSamples += pcm16kMonoChunk.Length;

                while (_recognizer.IsReady(_stream))
                {
                    _recognizer.Decode(_stream);
                }

                stopwatch.Stop();
                _currentResult = BuildResult(stopwatch.Elapsed, isFinal: false);
            }

            return ValueTask.CompletedTask;
        }

        public StreamingAsrResult GetCurrentResult()
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                return _currentResult;
            }
        }

        public ValueTask<StreamingAsrResult> CompleteAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            lock (_syncRoot)
            {
                ThrowIfDisposed();

                if (_isCompleted)
                {
                    return ValueTask.FromResult(_currentResult);
                }

                var stopwatch = Stopwatch.StartNew();
                _stream.InputFinished();

                while (_recognizer.IsReady(_stream))
                {
                    _recognizer.Decode(_stream);
                }

                stopwatch.Stop();
                _isCompleted = true;
                _currentResult = BuildResult(stopwatch.Elapsed, isFinal: true);
                return ValueTask.FromResult(_currentResult);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _stream.Dispose();
            _disposed = true;

            // 归还识别器租约：若引擎已卸载/替换且这是最后一个会话，由这里兜底释放识别器。
            _releaseLease();
        }

        private StreamingAsrResult BuildResult(TimeSpan decodeLatency, bool isFinal)
        {
            var text = _recognizer.GetResult(_stream).Text?.Trim() ?? string.Empty;
            return new StreamingAsrResult
            {
                Text = text,
                StableText = text,
                PartialText = text,
                IsFinal = isFinal,
                AudioDuration = TimeSpan.FromSeconds(_totalSamples / 16000d),
                DecodeLatency = decodeLatency
            };
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SherpaStreamingParaformerSession));
            }
        }
    }
}
