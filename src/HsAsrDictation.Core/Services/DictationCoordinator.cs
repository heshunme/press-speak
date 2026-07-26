using HsAsrDictation.Asr;
using HsAsrDictation.Audio;
using HsAsrDictation.Foreground;
using HsAsrDictation.Insertion;
using HsAsrDictation.Logging;
using HsAsrDictation.Models;
using HsAsrDictation.Notifications;
using HsAsrDictation.PostProcessing.Abstractions;
using HsAsrDictation.PostProcessing.Engine;
using HsAsrDictation.Settings;

namespace HsAsrDictation.Services;

/// <summary>
/// 主链路编排：热键按下创建 <see cref="RecordingSession"/>，热键释放/超时/异常触发结束流程。
/// 并发模型：<see cref="_sync"/> 是唯一状态锁，保护 <see cref="_state"/> 与 <see cref="_session"/>
/// 的迁移；会话内部的流式管线状态由会话对象自持。所有状态迁移（claim 开始、claim 结束、回到
/// Idle）都在锁内原子完成，IO 在锁外执行；结束流程先等待启动 IO 完成，因此"热键释放先于启动
/// 完成"也能正确停止录音。
/// </summary>
public sealed class DictationCoordinator
{
    private readonly SettingsService _settingsService;
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly IModelProvisioningService _modelProvisioningService;
    private readonly IPunctuationModelProvisioningService _punctuationModelProvisioningService;
    private readonly IAsrEngine _asrEngine;
    private readonly IStreamingAsrEngine _streamingAsrEngine;
    private readonly IPunctuationService _punctuationService;
    private readonly IPostProcessingService _postProcessingService;
    private readonly ModelResidencyManager _modelResidencyManager;
    private readonly IForegroundContextService _foregroundContextService;
    private readonly ITextInsertionService _textInsertionService;
    private readonly INotificationService _notificationService;
    private readonly LocalLogService _logger;
    private readonly TimeSpan? _hotkeyReleaseTailDurationOverride;

    private readonly object _sync = new();
    private readonly SemaphoreSlim _punctuationReloadLock = new(1, 1);
    private DictationState _state = DictationState.Idle;
    private volatile RecordingSession? _session;

    public DictationCoordinator(
        SettingsService settingsService,
        IAudioCaptureService audioCaptureService,
        IModelProvisioningService modelProvisioningService,
        IPunctuationModelProvisioningService punctuationModelProvisioningService,
        IAsrEngine asrEngine,
        IStreamingAsrEngine streamingAsrEngine,
        IPunctuationService punctuationService,
        IPostProcessingService postProcessingService,
        IForegroundContextService foregroundContextService,
        ITextInsertionService textInsertionService,
        INotificationService notificationService,
        LocalLogService logger,
        TimeSpan? hotkeyReleaseTailDuration = null)
    {
        if (hotkeyReleaseTailDuration is { } tailDuration && tailDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(hotkeyReleaseTailDuration), "尾录时长不能为负数。");
        }

        _settingsService = settingsService;
        _audioCaptureService = audioCaptureService;
        _modelProvisioningService = modelProvisioningService;
        _punctuationModelProvisioningService = punctuationModelProvisioningService;
        _asrEngine = asrEngine;
        _streamingAsrEngine = streamingAsrEngine;
        _punctuationService = punctuationService;
        _postProcessingService = postProcessingService;
        _modelResidencyManager = new ModelResidencyManager(
            _modelProvisioningService,
            _asrEngine,
            _streamingAsrEngine);
        _foregroundContextService = foregroundContextService;
        _textInsertionService = textInsertionService;
        _notificationService = notificationService;
        _logger = logger;
        _hotkeyReleaseTailDurationOverride = hotkeyReleaseTailDuration;

        _audioCaptureService.AudioChunkAvailable += OnAudioChunkAvailable;
        _audioCaptureService.RecordingStopped += OnAudioCaptureStopped;
    }

    public event EventHandler<DictationStatus>? StateChanged;

    public async Task ToggleRecordingAsync()
    {
        bool recording;
        lock (_sync)
        {
            recording = _state == DictationState.Recording;
        }

        if (recording)
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
            var result = await _modelResidencyManager.EnsureModeReadyAsync(
                _settingsService.Current.RecognitionMode,
                downloadIfMissing,
                reinitialize,
                allowUnload: IsIdle(),
                ct);

            if (!result.Success)
            {
                _notificationService.Warn(AppInfo.Title, result.ErrorMessage ?? "模型未就绪。");
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("准备模型失败。", ex);
            _notificationService.Error(AppInfo.Title, $"模型准备失败：{ex.Message}");
        }
    }

    public async Task RedownloadModelAsync(bool reinitialize = true, CancellationToken ct = default)
    {
        try
        {
            var result = await _modelResidencyManager.RedownloadModeAsync(
                _settingsService.Current.RecognitionMode,
                reinitialize,
                allowUnload: IsIdle(),
                ct);

            if (!result.Success)
            {
                _notificationService.Warn(AppInfo.Title, result.ErrorMessage ?? "模型未就绪。");
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("重新下载模型失败。", ex);
            _notificationService.Error(AppInfo.Title, $"重新下载模型失败：{ex.Message}");
        }
    }

    public async Task EnsurePunctuationReadyAsync(
        bool downloadIfMissing,
        bool reinitialize = false,
        CancellationToken ct = default)
    {
        // 序列化并发调用（启动 + 设置保存可能重叠），FIFO 保证最后一次调用的结果生效。
        await _punctuationReloadLock.WaitAsync(ct);
        try
        {
            if (!_settingsService.Current.EnablePunctuation)
            {
                await Task.Run(() =>
                {
                    _punctuationService.Reload(new PunctuationRuntimeOptions
                    {
                        Enabled = false,
                        ModelPath = string.Empty,
                        NumThreads = 1
                    });
                }, ct);
                return;
            }

            var ready = await _punctuationModelProvisioningService.EnsureReadyAsync(downloadIfMissing, ct);
            var modelDirectory = ready.ModelDirectory ?? PunctuationModelManifest.ModelDirectory;
            var modelPath = Path.Combine(modelDirectory, PunctuationModelManifest.RequiredFileName);

            // 下载可能耗时很久，期间用户可能已关闭标点；重读当前设置，避免用过期快照重新启用。
            if (!_settingsService.Current.EnablePunctuation)
            {
                await Task.Run(() =>
                {
                    _punctuationService.Reload(new PunctuationRuntimeOptions
                    {
                        Enabled = false,
                        ModelPath = string.Empty,
                        NumThreads = 1
                    });
                }, ct);
                return;
            }

            if (reinitialize || !_punctuationService.IsReady)
            {
                await Task.Run(() =>
                {
                    _punctuationService.Reload(new PunctuationRuntimeOptions
                    {
                        Enabled = true,
                        ModelPath = modelPath,
                        NumThreads = 1
                    });
                }, ct);
            }

            if (_punctuationService.IsReady)
            {
                _logger.Info($"标点模型已就绪：{modelDirectory}");
                return;
            }

            var errorMessage = ready.ErrorMessage ?? "标点模型未就绪。";
            _logger.Warn(errorMessage);
            _notificationService.Warn(AppInfo.Title, $"{errorMessage} 听写将回退为原始文本。");
        }
        catch (Exception ex)
        {
            _logger.Error("准备标点模型失败。", ex);
            _notificationService.Error(AppInfo.Title, $"标点模型准备失败：{ex.Message}");
        }
        finally
        {
            _punctuationReloadLock.Release();
        }
    }

    public async Task BeginRecordingAsync()
    {
        if (TryCancelPendingTailFinalize())
        {
            return;
        }

        RecordingSession session;
        lock (_sync)
        {
            if (_state != DictationState.Idle || _session is not null)
            {
                return;
            }

            var settings = _settingsService.Current;
            session = new RecordingSession(settings, GetHotkeyReleaseTailDuration(settings));
            _session = session;
            _state = DictationState.Recording;
        }

        try
        {
            await StartSessionAsync(session);
            session.MarkStartSucceeded();
            // 状态迁移在锁内已完成（保证释放路径能命中会话）；对 UI 的发布推迟到
            // 启动真正成功之后，避免启动失败/缓慢时托盘与浮窗错误显示"录音中"。
            PublishStatus();
        }
        catch (Exception ex)
        {
            session.MarkStartFailed(ex);
            _logger.Error("开始录音失败。", ex);
            _notificationService.Error(AppInfo.Title, $"录音启动失败：{ex.Message}");
            await AbortSessionAsync(session);
        }
    }

    public Task FinalizeRecordingAfterHotkeyReleaseAsync()
    {
        RecordingSession session;
        CancellationTokenSource delayedFinalizeCts;
        lock (_sync)
        {
            if (_session is not { } currentSession ||
                _state != DictationState.Recording ||
                currentSession.FinalizationInProgress ||
                currentSession.PendingTailFinalize is not null)
            {
                return Task.CompletedTask;
            }

            session = currentSession;
            delayedFinalizeCts = new CancellationTokenSource();
            session.PendingTailFinalize = delayedFinalizeCts;
        }

        _logger.Info($"热键已释放，将在 {session.HotkeyReleaseTailDuration.TotalMilliseconds:0} ms 后结束录音。");
        _ = RunHotkeyReleaseFinalizeAsync(session, delayedFinalizeCts);
        return Task.CompletedTask;
    }

    public async Task FinalizeRecordingAsync()
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        if (!TryEnterFinalization(session, expectedPendingTailFinalize: null, out var pendingTailToCancel))
        {
            return;
        }

        if (pendingTailToCancel is not null)
        {
            CancelPendingTail(pendingTailToCancel);
            _logger.Info("已取消待执行的尾录停止，立即结束录音。");
        }

        await FinalizeRecordingCoreAsync(session, aborted: false);
    }

    private async Task StartSessionAsync(RecordingSession session)
    {
        session.SetCaptureContext(_foregroundContextService.Capture());

        if (session.Settings.RecognitionMode != RecognitionMode.NonStreaming)
        {
            try
            {
                await session.InitializeStreamingAsync(_streamingAsrEngine, PublishStatus, _logger);
            }
            catch (Exception ex)
            {
                session.MarkStreamingFailed();
                _logger.Warn($"流式识别初始化失败：{ex.Message}");

                if (session.Settings.RecognitionMode == RecognitionMode.StreamingOnly)
                {
                    throw;
                }
            }
        }

        // 音频启动必须是最后一步：结束流程依赖"启动失败 ⇒ 音频未启动"，失败时跳过 StopAsync。
        await _audioCaptureService.StartAsync(session.Settings.PreferredInputDeviceName);
    }

    private async Task AbortSessionAsync(RecordingSession session)
    {
        if (!TryEnterFinalization(session, expectedPendingTailFinalize: null, out var pendingTailToCancel))
        {
            // 结束流程已被其他路径（热键释放/停止回调）认领，由其完成清理。
            return;
        }

        if (pendingTailToCancel is not null)
        {
            CancelPendingTail(pendingTailToCancel);
        }

        await FinalizeRecordingCoreAsync(session, aborted: true);
    }

    private async Task RunHotkeyReleaseFinalizeAsync(
        RecordingSession session,
        CancellationTokenSource delayedFinalizeCts)
    {
        try
        {
            await Task.Delay(session.HotkeyReleaseTailDuration, delayedFinalizeCts.Token);

            if (!TryEnterFinalization(session, delayedFinalizeCts, out _))
            {
                return;
            }

            _logger.Info("尾录结束，开始最终处理。");
            await FinalizeRecordingCoreAsync(session, aborted: false);
        }
        catch (OperationCanceledException) when (delayedFinalizeCts.IsCancellationRequested)
        {
        }
        finally
        {
            delayedFinalizeCts.Dispose();
        }
    }

    private async Task FinalizeRecordingCoreAsync(RecordingSession session, bool aborted)
    {
        try
        {
            if (!aborted)
            {
                PublishStatus();
            }

            var startFailed = aborted;
            if (!startFailed)
            {
                try
                {
                    await session.StartCompletion;
                }
                catch
                {
                    startFailed = true;
                }
            }

            if (startFailed)
            {
                // 启动失败 ⇒ 音频未启动，无需 StopAsync；流式管线由 finally 统一收尾。
                return;
            }

            var audio = await _audioCaptureService.StopAsync();
            var streamingFinalText = await session.CompleteStreamingAsync();

            if (audio.Duration < TimeSpan.FromMilliseconds(150))
            {
                _logger.Info("录音时长过短，已忽略本次听写。");
                return;
            }

            var finalText = session.Settings.RecognitionMode switch
            {
                RecognitionMode.NonStreaming => await DecodeOfflineAsync(audio),
                RecognitionMode.Hybrid => await DecodeHybridAsync(audio, session, streamingFinalText),
                RecognitionMode.StreamingOnly => DecodeStreamingOnly(session, streamingFinalText),
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(finalText))
            {
                return;
            }

            SetState(DictationState.Inserting);
            finalText = _punctuationService.TryAddPunctuation(finalText);
            if (session.Settings.EnablePostProcessingRules)
            {
                finalText = _postProcessingService.TryProcess(finalText, CreateRuleExecutionContext(
                    session.CaptureContext ?? _foregroundContextService.Capture()));
            }

            var insertionResult = await _textInsertionService.InsertAsync(
                finalText,
                session.CaptureContext ?? _foregroundContextService.Capture());

            if (!insertionResult.Success)
            {
                _notificationService.Warn(
                    AppInfo.Title,
                    insertionResult.Error ?? "文本注入失败。");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("处理录音失败。", ex);
            _notificationService.Error(AppInfo.Title, $"听写失败：{ex.Message}");
        }
        finally
        {
            // 无论哪条路径（含 StopAsync 抛异常），都先把流式管线收尾（幂等），
            // 确保循环任务退出后再 Dispose 会话，避免释放正在使用的原生流。
            try
            {
                await session.CompleteStreamingAsync();
            }
            catch (Exception completeException)
            {
                _logger.Error("结束流式识别管线失败。", completeException);
            }

            session.Dispose();
            lock (_sync)
            {
                if (ReferenceEquals(_session, session))
                {
                    _session = null;
                }

                _state = DictationState.Idle;
            }

            PublishStatus();
        }
    }

    private void OnAudioChunkAvailable(object? sender, AudioChunkAvailableEventArgs e)
    {
        _session?.AcceptChunk(e.Samples);
    }

    private void OnAudioCaptureStopped(object? sender, AudioCaptureStoppedEventArgs e)
    {
        lock (_sync)
        {
            if (_session is not { } session ||
                _state != DictationState.Recording ||
                session.FinalizationInProgress)
            {
                return;
            }
        }

        if (e.Reason == AudioCaptureStopReason.MaxDurationReached)
        {
            _notificationService.Warn(
                AppInfo.Title,
                "已达到单次录音时长上限，当前内容会自动结束并继续写回。");
        }
        else if (e.Reason == AudioCaptureStopReason.Faulted)
        {
            _notificationService.Warn(
                AppInfo.Title,
                "录音被意外中断，将尝试保留已录到的内容。");
        }

        _ = FinalizeRecordingAsync();
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
            _notificationService.Warn(AppInfo.Title, asrResult.Error ?? "识别未返回文本。");
            return string.Empty;
        }

        return DictationTextNormalizer.Normalize(asrResult.Text);
    }

    private async Task<string> DecodeHybridAsync(
        RecordedAudio audio,
        RecordingSession session,
        string streamingFinalText)
    {
        var offlineText = await DecodeOfflineAsync(audio);
        if (!string.IsNullOrWhiteSpace(offlineText))
        {
            return offlineText;
        }

        if (session.StreamingFailed && string.IsNullOrWhiteSpace(streamingFinalText))
        {
            return string.Empty;
        }

        return streamingFinalText;
    }

    private string DecodeStreamingOnly(RecordingSession session, string streamingFinalText)
    {
        if (session.StreamingFailed)
        {
            _notificationService.Warn(AppInfo.Title, "流式识别失败，未生成可写回文本。");
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(streamingFinalText))
        {
            _notificationService.Warn(AppInfo.Title, "流式识别未返回文本。");
            return string.Empty;
        }

        return streamingFinalText;
    }

    private bool TryCancelPendingTailFinalize()
    {
        CancellationTokenSource pendingTailFinalize;

        lock (_sync)
        {
            if (_session is not { } session ||
                _state != DictationState.Recording ||
                session.FinalizationInProgress ||
                session.PendingTailFinalize is null)
            {
                return false;
            }

            pendingTailFinalize = session.PendingTailFinalize;
            session.PendingTailFinalize = null;
        }

        CancelPendingTail(pendingTailFinalize);
        _logger.Info("热键再次按下，已取消待执行的尾录停止。");
        return true;
    }

    private bool TryEnterFinalization(
        RecordingSession session,
        CancellationTokenSource? expectedPendingTailFinalize,
        out CancellationTokenSource? pendingTailToCancel)
    {
        lock (_sync)
        {
            pendingTailToCancel = null;

            if (!ReferenceEquals(_session, session) ||
                _state != DictationState.Recording ||
                session.FinalizationInProgress)
            {
                return false;
            }

            if (expectedPendingTailFinalize is null)
            {
                pendingTailToCancel = session.PendingTailFinalize;
                session.PendingTailFinalize = null;
            }
            else
            {
                if (!ReferenceEquals(session.PendingTailFinalize, expectedPendingTailFinalize))
                {
                    return false;
                }

                session.PendingTailFinalize = null;
            }

            session.FinalizationInProgress = true;
            _state = DictationState.Finalizing;
            return true;
        }
    }

    private static void CancelPendingTail(CancellationTokenSource pendingTailFinalize)
    {
        try
        {
            pendingTailFinalize.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 尾录任务在 TryEnterFinalization 失败后会释放自己的 CTS，与此处的取消存在竞态；
            // 结束流程已由当前路径接管，忽略即可。
        }
    }

    private bool IsIdle()
    {
        lock (_sync)
        {
            return _state == DictationState.Idle;
        }
    }

    private void SetState(DictationState state)
    {
        lock (_sync)
        {
            _state = state;
        }

        PublishStatus();
    }

    private void PublishStatus()
    {
        DictationStatus status;
        lock (_sync)
        {
            var session = _session;
            status = new DictationStatus
            {
                State = _state,
                Mode = (session?.Settings ?? _settingsService.Current).RecognitionMode,
                OverlayText = _state.ToDisplayText(),
                PreviewText = _state == DictationState.Recording
                    ? session?.PreviewText ?? string.Empty
                    : string.Empty
            };
        }

        StateChanged?.Invoke(this, status);
    }

    private TimeSpan GetHotkeyReleaseTailDuration(AppSettings settings)
    {
        return _hotkeyReleaseTailDurationOverride
            ?? TimeSpan.FromMilliseconds(settings.HotkeyReleaseTailDurationMilliseconds);
    }

    private static RuleExecutionContext CreateRuleExecutionContext(ForegroundContext context)
    {
        return new RuleExecutionContext
        {
            ProcessName = string.IsNullOrWhiteSpace(context.ProcessName) ? null : context.ProcessName,
            WindowTitle = string.IsNullOrWhiteSpace(context.WindowTitle) ? null : context.WindowTitle,
            IsPasswordField = context.IsPasswordField
        };
    }
}
