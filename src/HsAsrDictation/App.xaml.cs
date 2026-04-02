using System.Windows;
using HsAsrDictation.Asr;
using HsAsrDictation.Audio;
using HsAsrDictation.Foreground;
using HsAsrDictation.Hotkeys;
using HsAsrDictation.Insertion;
using HsAsrDictation.Logging;
using HsAsrDictation.Models;
using HsAsrDictation.Notifications;
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
    private IAsrEngine? _asrEngine;
    private ForegroundContextService? _foregroundContextService;
    private ITextInsertionService? _textInsertionService;
    private DictationCoordinator? _coordinator;
    private TrayIconService? _trayIconService;
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
        _asrEngine = new SherpaFunAsrNanoEngine(_modelProvisioningService, _settingsService, _logger);
        _foregroundContextService = new ForegroundContextService(_logger);
        _textInsertionService = new TextInsertionService(_settingsService, _foregroundContextService, _logger);
        _coordinator = new DictationCoordinator(
            _settingsService,
            _audioCaptureService,
            _modelProvisioningService,
            _asrEngine,
            _foregroundContextService,
            _textInsertionService,
            _notificationService,
            _logger);

        _trayIconService = new TrayIconService(_notificationService, _logger);
        _trayIconService.SettingsRequested += (_, _) => OpenSettingsWindow();
        _trayIconService.ModelDownloadRequested += async (_, _) => await _coordinator.RedownloadModelAsync();
        _trayIconService.ToggleRecordingRequested += async (_, _) => await _coordinator.ToggleRecordingAsync();
        _trayIconService.ExitRequested += (_, _) => Shutdown();

        _coordinator.StateChanged += (_, state) => _trayIconService.SetStatus(state.ToDisplayText());

        _hotkeyManager.Pressed += async (_, _) => await _coordinator.BeginRecordingAsync();
        _hotkeyManager.Released += async (_, _) => await _coordinator.FinalizeRecordingAsync();
        _hotkeyManager.Start(_settingsService.Current.Hotkey);

        _ = _coordinator.EnsureModelReadyAsync(downloadIfMissing: _settingsService.Current.AutoDownloadModel);
        _trayIconService.SetStatus("就绪");
        _notificationService.Info("HsAsrDictation", "应用已启动，托盘常驻。");
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
        _trayIconService?.Dispose();
        _logger?.Dispose();
        base.OnExit(e);
    }

    private void OpenSettingsWindow()
    {
        if (_settingsService is null || _audioCaptureService is null || _hotkeyManager is null || _coordinator is null)
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
                _audioCaptureService.GetInputDevices());

            _settingsWindow.SettingsSaved += (_, updatedSettings) =>
            {
                _settingsService.Save(updatedSettings);
                _hotkeyManager.UpdateGesture(updatedSettings.Hotkey);
                _ = _coordinator.EnsureModelReadyAsync(downloadIfMissing: false, reinitialize: true);
            };

            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        });
    }
}
