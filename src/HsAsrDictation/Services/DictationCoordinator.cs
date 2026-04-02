using System.Text.RegularExpressions;
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
    private readonly ForegroundContextService _foregroundContextService;
    private readonly ITextInsertionService _textInsertionService;
    private readonly NotificationService _notificationService;
    private readonly LocalLogService _logger;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

    private DictationState _state = DictationState.Idle;
    private ForegroundContext? _captureContext;

    public DictationCoordinator(
        SettingsService settingsService,
        IAudioCaptureService audioCaptureService,
        IModelProvisioningService modelProvisioningService,
        IAsrEngine asrEngine,
        ForegroundContextService foregroundContextService,
        ITextInsertionService textInsertionService,
        NotificationService notificationService,
        LocalLogService logger)
    {
        _settingsService = settingsService;
        _audioCaptureService = audioCaptureService;
        _modelProvisioningService = modelProvisioningService;
        _asrEngine = asrEngine;
        _foregroundContextService = foregroundContextService;
        _textInsertionService = textInsertionService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public event EventHandler<DictationState>? StateChanged;

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
            var ready = await _modelProvisioningService.EnsureReadyAsync(downloadIfMissing, ct);

            if (!ready.IsReady)
            {
                _notificationService.Warn("HsAsrDictation", ready.ErrorMessage ?? "模型未就绪。");
                return;
            }

            if (reinitialize || !_asrEngine.IsReady)
            {
                await _asrEngine.InitializeAsync(ct);
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
            var ready = await _modelProvisioningService.DownloadAsync(ct);

            if (!ready.IsReady)
            {
                _notificationService.Warn("HsAsrDictation", ready.ErrorMessage ?? "模型未就绪。");
                return;
            }

            if (reinitialize || !_asrEngine.IsReady)
            {
                await _asrEngine.InitializeAsync(ct);
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
                return;
            }

            _captureContext = _foregroundContextService.Capture();
            SetState(DictationState.Recording);
            await _audioCaptureService.StartAsync(_settingsService.Current.PreferredInputDeviceName);
            _notificationService.Info("HsAsrDictation", "正在录音...");
        }
        catch (Exception ex)
        {
            _logger.Error("开始录音失败。", ex);
            _notificationService.Error("HsAsrDictation", $"录音启动失败：{ex.Message}");
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

            if (audio.Duration < TimeSpan.FromMilliseconds(150))
            {
                _notificationService.Info("HsAsrDictation", "录音过短，已忽略。");
                return;
            }

            var trimmed = AudioSilenceTrimmer.Trim(audio.Samples, 16000);
            if (trimmed.Length < 1600)
            {
                _notificationService.Info("HsAsrDictation", "未检测到清晰语音。");
                return;
            }

            SetState(DictationState.Decoding);
            _notificationService.Info("HsAsrDictation", "识别中...");
            var asrResult = await _asrEngine.TranscribeAsync(trimmed);

            if (!asrResult.Success || string.IsNullOrWhiteSpace(asrResult.Text))
            {
                _notificationService.Warn("HsAsrDictation", asrResult.Error ?? "识别未返回文本。");
                return;
            }

            SetState(DictationState.Inserting);
            var normalizedText = NormalizeText(asrResult.Text);
            var insertionResult = await _textInsertionService.InsertAsync(
                normalizedText,
                _captureContext ?? _foregroundContextService.Capture());

            if (!insertionResult.Success)
            {
                _notificationService.Warn(
                    "HsAsrDictation",
                    insertionResult.Error ?? "文本注入失败。");
                return;
            }

            _notificationService.Info(
                "HsAsrDictation",
                insertionResult.Method == "Clipboard"
                    ? "已通过剪贴板回退插入文本。"
                    : "文本已插入。");
        }
        catch (Exception ex)
        {
            _logger.Error("处理录音失败。", ex);
            _notificationService.Error("HsAsrDictation", $"听写失败：{ex.Message}");
        }
        finally
        {
            _captureContext = null;
            SetState(DictationState.Idle);
            _sessionLock.Release();
        }
    }

    private void SetState(DictationState state)
    {
        _state = state;
        StateChanged?.Invoke(this, state);
    }

    private static string NormalizeText(string input)
    {
        var collapsed = Regex.Replace(input.Trim(), @"\s+", " ");
        return collapsed.Replace(" ,", ",").Replace(" .", ".");
    }
}

public enum DictationState
{
    Idle,
    Recording,
    Finalizing,
    Decoding,
    Inserting
}

public static class DictationStateExtensions
{
    public static string ToDisplayText(this DictationState state) =>
        state switch
        {
            DictationState.Idle => "就绪",
            DictationState.Recording => "录音中",
            DictationState.Finalizing => "结束录音",
            DictationState.Decoding => "识别中",
            DictationState.Inserting => "回写中",
            _ => "未知状态"
        };
}
