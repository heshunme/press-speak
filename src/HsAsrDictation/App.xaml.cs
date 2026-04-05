using System.Windows;
using HsAsrDictation.Asr;
using HsAsrDictation.Audio;
using HsAsrDictation.Foreground;
using HsAsrDictation.Hotkeys;
using HsAsrDictation.Insertion;
using HsAsrDictation.Logging;
using HsAsrDictation.Models;
using HsAsrDictation.Notifications;
using HsAsrDictation.Overlay;
using HsAsrDictation.PostProcessing.Abstractions;
using HsAsrDictation.PostProcessing.Engine;
using HsAsrDictation.Services;
using HsAsrDictation.Settings;
using HsAsrDictation.Tray;
using HsAsrDictation.Views;

namespace HsAsrDictation;

public partial class App : System.Windows.Application
{
    private LocalLogService? _logger;
    private SettingsService? _settingsService;
    private NotificationService? _notificationService;
    private IHotkeyManager? _hotkeyManager;
    private IAudioCaptureService? _audioCaptureService;
    private IModelProvisioningService? _modelProvisioningService;
    private IPunctuationModelProvisioningService? _punctuationModelProvisioningService;
    private IAsrEngine? _asrEngine;
    private IStreamingAsrEngine? _streamingAsrEngine;
    private IPunctuationService? _punctuationService;
    private ForegroundContextService? _foregroundContextService;
    private ITextInsertionService? _textInsertionService;
    private IPostProcessingRuleRepository? _postProcessingRuleRepository;
    private IPostProcessingRuleFactory? _postProcessingRuleFactory;
    private IPostProcessingService? _postProcessingService;
    private DictationCoordinator? _coordinator;
    private TrayIconService? _trayIconService;
    private IStatusOverlayService? _statusOverlayService;
    private DictationOverlayController? _dictationOverlayController;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _logger = new LocalLogService(AppPaths.LogsDirectory);
        _settingsService = new SettingsService(AppPaths.SettingsFilePath, _logger);
        _settingsService.Load();

        _notificationService = new NotificationService();
        _hotkeyManager = new LowLevelKeyboardHotkeyManager(_logger);
        _audioCaptureService = new WaveInAudioCaptureService(_logger);
        _modelProvisioningService = new ModelProvisioningService(_settingsService, _logger);
        _punctuationModelProvisioningService = new PunctuationModelProvisioningService(_logger);
        _asrEngine = new SherpaFunAsrNanoEngine(_modelProvisioningService, _settingsService, _logger);
        _streamingAsrEngine = new SherpaStreamingParaformerEngine(_modelProvisioningService, _settingsService, _logger);
        _punctuationService = new SherpaOfflinePunctuationService(_logger);
        _foregroundContextService = new ForegroundContextService(_logger);
        _textInsertionService = new TextInsertionService(_settingsService, _foregroundContextService, _logger);
        _postProcessingRuleRepository = new PostProcessingRuleRepository(AppPaths.PostProcessingUserRulesPath, _logger);
        _postProcessingRuleFactory = new PostProcessingRuleFactory(_logger);
        _postProcessingService = new PostProcessingService(_postProcessingRuleRepository, _postProcessingRuleFactory, _logger);
        _statusOverlayService = new StatusOverlayService();
        _dictationOverlayController = new DictationOverlayController(_statusOverlayService);
        _coordinator = new DictationCoordinator(
            _settingsService,
            _audioCaptureService,
            _modelProvisioningService,
            _punctuationModelProvisioningService,
            _asrEngine,
            _streamingAsrEngine,
            _punctuationService,
            _postProcessingService,
            _foregroundContextService,
            _textInsertionService,
            _notificationService,
            _logger);

        _trayIconService = new TrayIconService(_notificationService, _logger);
        _trayIconService.SettingsRequested += (_, _) => OpenSettingsWindow();
        _trayIconService.ModelDownloadRequested += async (_, _) => await _coordinator.RedownloadModelAsync();
        _trayIconService.ToggleRecordingRequested += async (_, _) => await _coordinator.ToggleRecordingAsync();
        _trayIconService.ExitRequested += (_, _) => Shutdown();

        _coordinator.StateChanged += (_, status) =>
            _ = Dispatcher.InvokeAsync(() =>
            {
                _trayIconService.SetStatus(status.OverlayText);
                _dictationOverlayController.Update(status);
            });

        _hotkeyManager.Pressed += async (_, _) => await _coordinator.BeginRecordingAsync();
        _hotkeyManager.Released += async (_, _) => await _coordinator.FinalizeRecordingAsync();
        _hotkeyManager.Start(_settingsService.Current.Hotkey);

        _ = _coordinator.EnsureModelReadyAsync(downloadIfMissing: _settingsService.Current.AutoDownloadModel);
        _ = _coordinator.EnsurePunctuationReadyAsync(downloadIfMissing: _settingsService.Current.AutoDownloadModel);
        _trayIconService.SetStatus("就绪");
        _dictationOverlayController.Update(new DictationStatus
        {
            State = DictationState.Idle,
            Mode = _settingsService.Current.RecognitionMode,
            OverlayText = DictationState.Idle.ToDisplayText()
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Close();
            _settingsWindow = null;
        }

        _hotkeyManager?.Dispose();
        _audioCaptureService?.Dispose();
        _asrEngine?.Dispose();
        _streamingAsrEngine?.Dispose();
        _punctuationService?.Dispose();
        _statusOverlayService?.Dispose();
        _trayIconService?.Dispose();
        _logger?.Dispose();
        base.OnExit(e);
    }

    private void OpenSettingsWindow()
    {
        if (_settingsService is null ||
            _audioCaptureService is null ||
            _hotkeyManager is null ||
            _coordinator is null ||
            _postProcessingRuleRepository is null ||
            _postProcessingService is null)
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_settingsWindow is not null && _settingsWindow.IsLoaded)
            {
                if (_settingsWindow.WindowState == WindowState.Minimized)
                {
                    _settingsWindow.WindowState = WindowState.Normal;
                }

                _settingsWindow.Show();
                _settingsWindow.Activate();
                _settingsWindow.Focus();
                return;
            }

            _settingsWindow = new SettingsWindow(
                _settingsService.Current,
                _audioCaptureService.GetInputDevices(),
                _hotkeyManager,
                _postProcessingRuleRepository,
                _postProcessingService);

            _settingsWindow.SettingsSaved += (_, updatedSettings) =>
            {
                _settingsService.Save(updatedSettings);
                _hotkeyManager.UpdateGesture(updatedSettings.Hotkey);
                _ = _coordinator.EnsureModelReadyAsync(downloadIfMissing: false, reinitialize: true);
                _ = _coordinator.EnsurePunctuationReadyAsync(
                    downloadIfMissing: updatedSettings.AutoDownloadModel,
                    reinitialize: true);
            };

            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        });
    }
}
