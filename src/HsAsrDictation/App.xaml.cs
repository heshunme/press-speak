using System.Windows;
using System.Windows.Forms;
using System.Security.Principal;
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
    private const string AppTitle = "HsAsrDictation";

    private LocalLogService? _logger;
    private SettingsService? _settingsService;
    private NotificationService? _notificationService;
    private LowLevelKeyboardEventSource? _keyboardEventSource;
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
    private ElevationService? _elevationService;
    private SingleInstanceCoordinator? _singleInstanceCoordinator;
    private IStartupRegistrationService? _startupRegistrationService;
    private StartupOptions _startupOptions = StartupOptions.Parse([]);
    private PrivilegeMode _currentPrivilegeMode;
    private NotificationMessage? _pendingStartupNotification;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _startupOptions = StartupOptions.Parse(e.Args);
        _logger = new LocalLogService(AppPaths.LogsDirectory);
        _elevationService = new ElevationService();
        _currentPrivilegeMode = _elevationService.IsRunningAsAdministrator()
            ? PrivilegeMode.Administrator
            : PrivilegeMode.Standard;

        _logger.Info(
            $"启动参数：admin={_startupOptions.RequestAdministrator}, autoStart={_startupOptions.IsAutoStart}, elevationApplied={_startupOptions.ElevationApplied}, startupTaskMaintenance={_startupOptions.StartupTaskMaintenance}, forwardedArgs={FormatForwardedArgs(_startupOptions.ForwardedArgs)}");
        _logger.Info($"当前运行模式：{_currentPrivilegeMode.ToDisplayText()}");

        if (_startupOptions.StartupTaskMaintenance != StartupTaskMaintenanceAction.None)
        {
            ExecuteStartupTaskMaintenance();
            return;
        }

        if (_startupOptions.IsElevationOriginCheck)
        {
            var originMatches = _startupOptions.IsElevationOriginCurrent(ResolveCurrentUserSid());
            _logger.Info($"管理员启动账号预检：matches={originMatches}");
            Shutdown(originMatches ? 0 : StartupOptions.ElevationOriginMismatchExitCode);
            return;
        }

        if (!_startupOptions.IsElevationOriginCurrent(ResolveCurrentUserSid()))
        {
            const string message =
                "管理员模式必须由当前 Windows 账号本人确认，不能在 UAC 中改用另一个管理员账号。";
            _logger.Error(message);
            System.Windows.MessageBox.Show(
                message,
                AppTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _startupRegistrationService = new StartupRegistrationService(
            new WindowsStartupRegistrationPlatform());

        var instanceRuntimeContext = InstanceRuntimeContext.CreateForCurrentUser();
        _singleInstanceCoordinator = new SingleInstanceCoordinator(instanceRuntimeContext, _logger);
        var instanceStartupResult = _singleInstanceCoordinator.CoordinateStartup(_currentPrivilegeMode, _startupOptions);
        if (!instanceStartupResult.ShouldContinueStartup)
        {
            if (!string.IsNullOrWhiteSpace(instanceStartupResult.ExitMessage))
            {
                System.Windows.MessageBox.Show(
                    instanceStartupResult.ExitMessage,
                    AppTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        if (_startupOptions.ShouldRestartAsAdministrator(_currentPrivilegeMode == PrivilegeMode.Administrator))
        {
            var elevationResult = _elevationService.RestartAsAdministrator();
            if (elevationResult.WasStarted)
            {
                _logger.Info("已请求以管理员模式重启当前应用。");
                Shutdown();
                return;
            }

            if (elevationResult.WasCanceled)
            {
                _logger.Warn($"管理员模式启动已取消：{elevationResult.Message}");
                _pendingStartupNotification = new NotificationMessage(
                    AppTitle,
                    "管理员模式启动已取消，当前将继续以普通权限运行。",
                    ToolTipIcon.Warning);
            }
            else if (elevationResult.WasFailed)
            {
                _logger.Error($"请求管理员模式启动失败：{elevationResult.Message}");
                _pendingStartupNotification = new NotificationMessage(
                    AppTitle,
                    $"请求管理员模式启动失败：{elevationResult.Message}",
                    ToolTipIcon.Error);
            }
        }

        _settingsService = new SettingsService(AppPaths.SettingsFilePath, _logger);
        _settingsService.Load();

        _notificationService = new NotificationService();
        _keyboardEventSource = new LowLevelKeyboardEventSource(_logger);
        _hotkeyManager = new LowLevelKeyboardHotkeyManager(_keyboardEventSource, _logger);
        _audioCaptureService = new WaveInAudioCaptureService(_settingsService, _logger);
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

        _trayIconService = new TrayIconService(_notificationService, _logger, _currentPrivilegeMode);
        _trayIconService.SettingsRequested += (_, _) => OpenSettingsWindow();
        _trayIconService.ModelDownloadRequested += async (_, _) => await _coordinator.RedownloadModelAsync();
        _trayIconService.ToggleRecordingRequested += async (_, _) => await _coordinator.ToggleRecordingAsync();
        _trayIconService.RestartAsAdministratorRequested += (_, _) => RestartAsAdministratorFromTray();
        _trayIconService.ExitRequested += (_, _) => Shutdown();

        _coordinator.StateChanged += (_, status) =>
            _ = Dispatcher.InvokeAsync(() =>
            {
                _trayIconService.SetStatus(status.OverlayText);
                _dictationOverlayController.Update(status);
            });

        _hotkeyManager.Pressed += async (_, _) => await _coordinator.BeginRecordingAsync();
        _hotkeyManager.Released += async (_, _) => await _coordinator.FinalizeRecordingAfterHotkeyReleaseAsync();
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

        if (_pendingStartupNotification is not null)
        {
            RaiseNotification(_pendingStartupNotification);
            _pendingStartupNotification = null;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Close();
            _settingsWindow = null;
        }

        _hotkeyManager?.Dispose();
        _keyboardEventSource?.Dispose();
        _audioCaptureService?.Dispose();
        _asrEngine?.Dispose();
        _streamingAsrEngine?.Dispose();
        _punctuationService?.Dispose();
        _statusOverlayService?.Dispose();
        _trayIconService?.Dispose();
        _singleInstanceCoordinator?.Dispose();
        _logger?.Dispose();
        base.OnExit(e);
    }

    private void OpenSettingsWindow()
    {
        if (_settingsService is null ||
            _logger is null ||
            _keyboardEventSource is null ||
            _audioCaptureService is null ||
            _hotkeyManager is null ||
            _coordinator is null ||
            _postProcessingRuleRepository is null ||
            _postProcessingService is null ||
            _startupRegistrationService is null)
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

            StartupRegistrationMode? startupRegistrationMode = null;
            string? startupRegistrationErrorMessage = null;
            try
            {
                var startupRegistrationState = _startupRegistrationService.GetState();
                startupRegistrationMode = startupRegistrationState.Mode;
                startupRegistrationErrorMessage = startupRegistrationState.Message;
            }
            catch (Exception ex)
            {
                _logger.Error("读取登录自启动设置失败。", ex);
                startupRegistrationErrorMessage = $"无法读取登录自启动状态，本次保存不会更改该设置：{ex.Message}";
            }

            _settingsWindow = new SettingsWindow(
                _settingsService.Current,
                _audioCaptureService.GetInputDevices(),
                _hotkeyManager,
                _keyboardEventSource,
                _logger,
                _postProcessingRuleRepository,
                _postProcessingService,
                _currentPrivilegeMode.ToDisplayText(),
                startupRegistrationMode,
                startupRegistrationErrorMessage);

            _settingsWindow.SettingsSaveRequested += (_, request) =>
            {
                var previousSettings = _settingsService.Current;
                var previousPostProcessingConfig = _postProcessingRuleRepository.Load();
                var startupRegistrationChanged = false;
                StartupRegistrationMode? previousStartupRegistrationMode = null;

                if (request.StartupRegistrationMode is { } requestedStartupRegistrationMode)
                {
                    previousStartupRegistrationMode = _startupRegistrationService.GetState().Mode;
                    var startupRegistrationResult = _startupRegistrationService.SetMode(
                        requestedStartupRegistrationMode);
                    if (!startupRegistrationResult.WasSuccessful)
                    {
                        var message = startupRegistrationResult.Message ?? "更新登录自启动设置失败。";
                        if (startupRegistrationResult.WasCanceled)
                        {
                            _logger.Warn($"登录自启动设置变更已取消：{message}");
                            throw new InvalidOperationException($"登录自启动设置未更改：{message}");
                        }

                        _logger.Error($"更新登录自启动设置失败：{message}");
                        throw new InvalidOperationException(message);
                    }

                    startupRegistrationChanged =
                        previousStartupRegistrationMode != requestedStartupRegistrationMode;
                    _logger.Info($"登录自启动设置已更新：{requestedStartupRegistrationMode}");
                }

                try
                {
                    _postProcessingRuleRepository.Save(request.PostProcessingConfig);
                    _settingsService.Save(request.Settings);
                }
                catch (Exception saveException)
                {
                    var rollbackFailures = new List<string>();
                    TryRollback(
                        "后处理规则",
                        () => _postProcessingRuleRepository.Save(previousPostProcessingConfig),
                        rollbackFailures);
                    TryRollback(
                        "应用设置",
                        () => _settingsService.Save(previousSettings),
                        rollbackFailures);

                    if (startupRegistrationChanged && previousStartupRegistrationMode is { } previousMode)
                    {
                        var rollbackResult = _startupRegistrationService.SetMode(previousMode);
                        if (!rollbackResult.WasSuccessful)
                        {
                            rollbackFailures.Add(
                                $"登录自启动：{rollbackResult.Message ?? "恢复失败"}");
                        }
                    }

                    var rollbackSuffix = rollbackFailures.Count == 0
                        ? "已恢复原设置。"
                        : $"部分回滚失败：{string.Join("；", rollbackFailures)}";
                    _logger.Error($"保存设置失败，{rollbackSuffix}", saveException);
                    throw new InvalidOperationException(
                        $"{saveException.Message} {rollbackSuffix}",
                        saveException);
                }

                _hotkeyManager.UpdateGesture(request.Settings.Hotkey);
                _ = _coordinator.EnsureModelReadyAsync(downloadIfMissing: false, reinitialize: true);
                _ = _coordinator.EnsurePunctuationReadyAsync(
                    downloadIfMissing: request.Settings.AutoDownloadModel,
                    reinitialize: true);
            };

            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        });
    }

    private static void TryRollback(
        string operation,
        Action rollback,
        ICollection<string> failures)
    {
        try
        {
            rollback();
        }
        catch (Exception ex)
        {
            failures.Add($"{operation}：{ex.Message}");
        }
    }

    private static string? ResolveCurrentUserSid()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value;
        }
        catch
        {
            return null;
        }
    }

    private void ExecuteStartupTaskMaintenance()
    {
        StartupRegistrationMaintenanceResult result;
        try
        {
            if (string.IsNullOrWhiteSpace(_startupOptions.StartupTaskUserSid))
            {
                result = StartupRegistrationMaintenanceResult.Failed(
                    "计划任务维护命令缺少目标用户 SID。");
            }
            else
            {
                var executablePath = Environment.ProcessPath
                    ?? throw new InvalidOperationException("无法解析当前可执行文件路径。");
                var platform = new WindowsStartupRegistrationPlatform(
                    executablePath,
                    _startupOptions.StartupTaskUserSid);
                result = platform.ExecuteMaintenance(_startupOptions.StartupTaskMaintenance);
            }
        }
        catch (Exception ex)
        {
            result = StartupRegistrationMaintenanceResult.Failed(ex.Message);
        }

        if (result.WasSuccessful)
        {
            _logger?.Info($"登录自启动计划任务维护完成：{_startupOptions.StartupTaskMaintenance}");
        }
        else
        {
            _logger?.Error(
                $"登录自启动计划任务维护失败：action={_startupOptions.StartupTaskMaintenance}, message={result.Message}");
        }

        Shutdown(WindowsStartupRegistrationPlatform.GetMaintenanceExitCode(result));
    }

    private void RestartAsAdministratorFromTray()
    {
        if (_elevationService is null || _notificationService is null || _logger is null)
        {
            return;
        }

        var result = _elevationService.RestartAsAdministrator();
        if (result.WasStarted)
        {
            _logger.Info("托盘已触发管理员模式重启。");
            Shutdown();
            return;
        }

        if (result.WasCanceled)
        {
            _logger.Warn($"托盘管理员模式启动已取消：{result.Message}");
            _notificationService.Warn(AppTitle, "管理员模式启动已取消，当前实例将继续运行。");
            return;
        }

        if (result.WasFailed)
        {
            _logger.Error($"托盘请求管理员模式启动失败：{result.Message}");
            _notificationService.Error(AppTitle, $"请求管理员模式启动失败：{result.Message}");
        }
    }

    private void RaiseNotification(NotificationMessage message)
    {
        if (_notificationService is null)
        {
            return;
        }

        switch (message.Icon)
        {
            case ToolTipIcon.Warning:
                _notificationService.Warn(message.Title, message.Message);
                break;
            case ToolTipIcon.Error:
                _notificationService.Error(message.Title, message.Message);
                break;
            default:
                _notificationService.Info(message.Title, message.Message);
                break;
        }
    }

    private static string FormatForwardedArgs(IReadOnlyList<string> args) =>
        args.Count == 0 ? "[]" : $"[{string.Join(", ", args)}]";
}
