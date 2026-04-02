using System.Text.RegularExpressions;
using System.Threading.Channels;
using HsAsrDictation.Asr;
using HsAsrDictation.Audio;
using HsAsrDictation.Foreground;
using HsAsrDictation.Insertion;
using HsAsrDictation.Logging;
using HsAsrDictation.Models;
using HsAsrDictation.Notifications;
using HsAsrDictation.Settings;

namespace HsAsrDictation.Services;

public sealed class DictationCoordinator
{
    private readonly SettingsService _settingsService;
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly IModelProvisioningService _modelProvisioningService;
    private readonly IAsrEngine _asrEngine;
    private readonly IStreamingAsrEngine _streamingAsrEngine;
    private readonly ForegroundContextService _foregroundContextService;
    private readonly ITextInsertionService _textInsertionService;
    private readonly NotificationService _notificationService;
    private readonly LocalLogService _logger;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

    private DictationState _state = DictationState.Idle;
    private ForegroundContext? _captureContext;
    private Channel<float[]>? _streamingChannel;
    private Task? _streamingLoopTask;
    private IStreamingAsrSession? _streamingSession;
    private string _streamingPreviewText = string.Empty;
    private string _streamingFinalText = string.Empty;
    private bool _streamingFailed;

    public DictationCoordinator(
        SettingsService settingsService,
        IAudioCaptureService audioCaptureService,
        IModelProvisioningService modelProvisioningService,
        IAsrEngine asrEngine,
        IStreamingAsrEngine streamingAsrEngine,
        ForegroundContextService foregroundContextService,
        ITextInsertionService textInsertionService,
        NotificationService notificationService,
        LocalLogService logger)
    {
        _settingsService = settingsService;
        _audioCaptureService = audioCaptureService;
        _modelProvisioningService = modelProvisioningService;
        _asrEngine = asrEngine;
        _streamingAsrEngine = streamingAsrEngine;
        _foregroundContextService = foregroundContextService;
        _textInsertionService = textInsertionService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public event EventHandler<DictationStatus>? StateChanged;

    public async Task ToggleRecordingAsync()
    {
        if (_state == DictationState.Recording)
        {
            await FinalizeRecordingAsync();
        }
        else
        {
            await BeginRecordingAsync();
        }
    }

    public async Task EnsureModelReadyAsync(bool downloadIfMissing, bool reinitialize = false, CancellationToken ct = default)
    {
        try
        {
            var settings = _settingsService.Current;
            var offlineReady = await _modelProvisioningService.EnsureReadyAsync(
                AsrModelKind.Offline,
                downloadIfMissing,
                ct);

            if (!offlineReady.IsReady)
            {
                _notificationService.Warn("HsAsrDictation", offlineReady.ErrorMessage ?? "离线模型未就绪。");
                return;
            }

            if (reinitialize || !_asrEngine.IsReady)
            {
                await _asrEngine.InitializeAsync(ct);
            }

            if (settings.RecognitionMode == RecognitionMode.NonStreaming)
            {
                return;
            }

            var streamingReady = await _modelProvisioningService.EnsureReadyAsync(
                AsrModelKind.Streaming,
                downloadIfMissing,
                ct);

            if (!streamingReady.IsReady)
            {
                _logger.Warn(streamingReady.ErrorMessage ?? "流式模型未就绪。");
                return;
            }

            if (reinitialize || !_streamingAsrEngine.IsReady)
            {
                await _streamingAsrEngine.InitializeAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("准备模型失败。", ex);
            _notificationService.Error("HsAsrDictation", $"模型准备失败：{ex.Message}");
        }
    }

    public async Task RedownloadModelAsync(bool reinitialize = true, CancellationToken ct = default)
    {
        try
        {
            var settings = _settingsService.Current;
            var offlineReady = await _modelProvisioningService.DownloadAsync(AsrModelKind.Offline, ct);

            if (!offlineReady.IsReady)
            {
                _notificationService.Warn("HsAsrDictation", offlineReady.ErrorMessage ?? "离线模型未就绪。");
                return;
            }

            if (reinitialize || !_asrEngine.IsReady)
            {
                await _asrEngine.InitializeAsync(ct);
            }

            if (settings.RecognitionMode == RecognitionMode.NonStreaming)
            {
                return;
            }

            var streamingReady = await _modelProvisioningService.DownloadAsync(AsrModelKind.Streaming, ct);

            if (!streamingReady.IsReady)
            {
                _notificationService.Warn("HsAsrDictation", streamingReady.ErrorMessage ?? "流式模型未就绪。");
                return;
            }

            if (reinitialize || !_streamingAsrEngine.IsReady)
            {
                await _streamingAsrEngine.InitializeAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("重新下载模型失败。", ex);
            _notificationService.Error("HsAsrDictation", $"重新下载模型失败：{ex.Message}");
        }
    }

    public async Task BeginRecordingAsync()
    {
        if (!await _sessionLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            if (_state != DictationState.Idle)
            {
                _sessionLock.Release();
                return;
            }

            ResetStreamingSessionState();
            _captureContext = _foregroundContextService.Capture();

            await InitializeStreamingSessionIfNeededAsync();
            _audioCaptureService.AudioChunkAvailable += OnAudioChunkAvailable;
            await _audioCaptureService.StartAsync(_settingsService.Current.PreferredInputDeviceName);
            SetState(DictationState.Recording);
        }
        catch (Exception ex)
        {
            _logger.Error("开始录音失败。", ex);
            _notificationService.Error("HsAsrDictation", $"录音启动失败：{ex.Message}");
            _audioCaptureService.AudioChunkAvailable -= OnAudioChunkAvailable;
            if (_streamingChannel is not null)
            {
                _streamingChannel.Writer.TryComplete();
            }

            if (_streamingLoopTask is not null)
            {
                await _streamingLoopTask;
            }

            CleanupStreamingResources();
            _captureContext = null;
            SetState(DictationState.Idle);
            _sessionLock.Release();
        }
    }

    public async Task FinalizeRecordingAsync()
    {
        if (_state != DictationState.Recording)
        {
            return;
        }

        try
        {
            SetState(DictationState.Finalizing);
            var audio = await _audioCaptureService.StopAsync();
            _audioCaptureService.AudioChunkAvailable -= OnAudioChunkAvailable;
            await CompleteStreamingLoopAsync();

            if (audio.Duration < TimeSpan.FromMilliseconds(150))
            {
                _logger.Info("录音时长过短，已忽略本次听写。");
                return;
            }

            var settings = _settingsService.Current;
            var finalText = settings.RecognitionMode switch
            {
                RecognitionMode.NonStreaming => await DecodeOfflineAsync(audio),
                RecognitionMode.Hybrid => await DecodeHybridAsync(audio),
                RecognitionMode.StreamingOnly => DecodeStreamingOnly(),
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(finalText))
            {
                return;
            }

            SetState(DictationState.Inserting);
            var insertionResult = await _textInsertionService.InsertAsync(
                finalText,
                _captureContext ?? _foregroundContextService.Capture());

            if (!insertionResult.Success)
            {
                _notificationService.Warn(
                    "HsAsrDictation",
                    insertionResult.Error ?? "文本注入失败。");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("处理录音失败。", ex);
            _notificationService.Error("HsAsrDictation", $"听写失败：{ex.Message}");
        }
        finally
        {
            _audioCaptureService.AudioChunkAvailable -= OnAudioChunkAvailable;
            CleanupStreamingResources();
            _captureContext = null;
            SetState(DictationState.Idle);
            _sessionLock.Release();
        }
    }

    private async Task InitializeStreamingSessionIfNeededAsync()
    {
        var settings = _settingsService.Current;
        if (settings.RecognitionMode == RecognitionMode.NonStreaming)
        {
            return;
        }

        try
        {
            await _streamingAsrEngine.InitializeAsync();
            _streamingSession = _streamingAsrEngine.CreateSession();
            _streamingChannel = Channel.CreateUnbounded<float[]>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            _streamingLoopTask = Task.Run(RunStreamingLoopAsync);
        }
        catch (Exception ex)
        {
            _streamingFailed = true;
            _logger.Warn($"流式识别初始化失败：{ex.Message}");

            if (settings.RecognitionMode == RecognitionMode.StreamingOnly)
            {
                throw;
            }
        }
    }

    private async Task RunStreamingLoopAsync()
    {
        if (_streamingChannel is null || _streamingSession is null)
        {
            return;
        }

        try
        {
            await foreach (var chunk in _streamingChannel.Reader.ReadAllAsync())
            {
                await _streamingSession.AcceptAudioAsync(chunk);
                UpdateStreamingPreview(_streamingSession.GetCurrentResult());
            }

            var completed = await _streamingSession.CompleteAsync();
            UpdateStreamingPreview(completed);
            _streamingFinalText = NormalizeText(completed.Text);
        }
        catch (Exception ex)
        {
            _streamingFailed = true;
            _logger.Error("流式识别执行失败。", ex);
        }
    }

    private void OnAudioChunkAvailable(object? sender, AudioChunkAvailableEventArgs e)
    {
        if (_streamingChannel is null)
        {
            return;
        }

        _streamingChannel.Writer.TryWrite(e.Samples);
    }

    private async Task CompleteStreamingLoopAsync()
    {
        if (_streamingChannel is null)
        {
            return;
        }

        _streamingChannel.Writer.TryComplete();

        if (_streamingLoopTask is not null)
        {
            await _streamingLoopTask;
        }
    }

    private async Task<string> DecodeOfflineAsync(RecordedAudio audio)
    {
        var trimmed = AudioSilenceTrimmer.Trim(audio.Samples, 16000);
        if (trimmed.Length < 1600)
        {
            _logger.Info("未检测到清晰语音，已忽略本次听写。");
            return string.Empty;
        }

        SetState(DictationState.Decoding);
        var asrResult = await _asrEngine.TranscribeAsync(trimmed);

        if (!asrResult.Success || string.IsNullOrWhiteSpace(asrResult.Text))
        {
            _notificationService.Warn("HsAsrDictation", asrResult.Error ?? "识别未返回文本。");
            return string.Empty;
        }

        return NormalizeText(asrResult.Text);
    }

    private async Task<string> DecodeHybridAsync(RecordedAudio audio)
    {
        var offlineText = await DecodeOfflineAsync(audio);
        if (!string.IsNullOrWhiteSpace(offlineText))
        {
            return offlineText;
        }

        if (_streamingFailed && string.IsNullOrWhiteSpace(_streamingFinalText))
        {
            return string.Empty;
        }

        return _streamingFinalText;
    }

    private string DecodeStreamingOnly()
    {
        if (_streamingFailed)
        {
            _notificationService.Warn("HsAsrDictation", "流式识别失败，未生成可写回文本。");
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(_streamingFinalText))
        {
            _notificationService.Warn("HsAsrDictation", "流式识别未返回文本。");
            return string.Empty;
        }

        return _streamingFinalText;
    }

    private void UpdateStreamingPreview(StreamingAsrResult result)
    {
        if (!_settingsService.Current.EnableStreamingPreview)
        {
            return;
        }

        var preview = NormalizeText(result.Text);
        if (string.Equals(preview, _streamingPreviewText, StringComparison.Ordinal))
        {
            return;
        }

        _streamingPreviewText = preview;
        PublishStatus();
    }

    private void SetState(DictationState state)
    {
        _state = state;
        PublishStatus();
    }

    private void PublishStatus()
    {
        StateChanged?.Invoke(this, new DictationStatus
        {
            State = _state,
            Mode = _settingsService.Current.RecognitionMode,
            OverlayText = _state.ToDisplayText(),
            PreviewText = _state == DictationState.Recording ? _streamingPreviewText : string.Empty
        });
    }

    private void ResetStreamingSessionState()
    {
        _streamingPreviewText = string.Empty;
        _streamingFinalText = string.Empty;
        _streamingFailed = false;
    }

    private void CleanupStreamingResources()
    {
        _streamingChannel = null;
        _streamingLoopTask = null;
        _streamingSession?.Dispose();
        _streamingSession = null;
        ResetStreamingSessionState();
    }

    private static string NormalizeText(string input)
    {
        var collapsed = Regex.Replace(input.Trim(), @"\s+", " ");
        return collapsed.Replace(" ,", ",").Replace(" .", ".");
    }
}
