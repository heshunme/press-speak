using System.Diagnostics;
using System.IO;
using HsAsrDictation.Logging;
using HsAsrDictation.Models;
using HsAsrDictation.Settings;
using SherpaOnnx;

namespace HsAsrDictation.Asr;

public sealed class SherpaStreamingParaformerEngine : IStreamingAsrEngine
{
    private readonly IModelProvisioningService _modelProvisioningService;
    private readonly SettingsService _settingsService;
    private readonly LocalLogService _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly object _recognizerSwapLock = new();
    private OnlineRecognizer? _recognizer;
    private string? _activeModelDirectory;

    public SherpaStreamingParaformerEngine(
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
        // _initLock 序列化并发初始化（启动预热、设置保存 reinit、会话启动可能重叠），
        // 避免重复构建识别器或对同一实例双重释放。
        await _initLock.WaitAsync(ct);
        try
        {
            var ready = await _modelProvisioningService.EnsureReadyAsync(
                AsrModelKind.Streaming,
                _settingsService.Current.AutoDownloadModel,
                ct);

            if (!ready.IsReady || string.IsNullOrWhiteSpace(ready.ModelDirectory))
            {
                throw new InvalidOperationException(ready.ErrorMessage ?? "流式模型不可用。");
            }

            if (_recognizer is not null &&
                string.Equals(_activeModelDirectory, ready.ModelDirectory, StringComparison.OrdinalIgnoreCase))
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

            OnlineRecognizer? previous;
            lock (_recognizerSwapLock)
            {
                previous = _recognizer;
                _recognizer = recognizer;
                _activeModelDirectory = ready.ModelDirectory;
            }

            previous?.Dispose();
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
            if (_recognizer is null)
            {
                throw new InvalidOperationException("流式识别器未初始化。");
            }

            return new SherpaStreamingParaformerSession(_recognizer);
        }
    }

    public void Dispose()
    {
        Unload();
        _initLock.Dispose();
    }

    public void Unload()
    {
        OnlineRecognizer? previous;
        lock (_recognizerSwapLock)
        {
            previous = _recognizer;
            _recognizer = null;
            _activeModelDirectory = null;
        }

        previous?.Dispose();
    }

    private sealed class SherpaStreamingParaformerSession : IStreamingAsrSession
    {
        private readonly OnlineRecognizer _recognizer;
        private readonly OnlineStream _stream;
        private readonly object _syncRoot = new();
        private StreamingAsrResult _currentResult = new();
        private int _totalSamples;
        private bool _isCompleted;
        private bool _disposed;

        public SherpaStreamingParaformerSession(OnlineRecognizer recognizer)
        {
            _recognizer = recognizer;
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
