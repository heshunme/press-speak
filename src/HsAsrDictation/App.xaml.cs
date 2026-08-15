using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using System.Security.Principal;
using HsAsrDictation.Asr;
using HsAsrDictation.Audio;
using HsAsrDictation.Foreground;
using HsAsrDictation.Hotkeys;
using HsAsrDictation.Insertion;
using HsAsrDictation.Logging;
using HsAsrDictation.Media;
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
    private MediaPlaybackPauseService? _mediaPlaybackPauseService;
    private TrayIconService? _trayIconService;
    private IStatusOverlayService? _statusOverlayService;
    private DictationOverlayController? _dictationOverlayController;
    private SettingsWindow? _settingsWindow;
    private ElevationService? _elevationService;
    private SingleInstanceCoordinator? _singleInstanceCoordinator;
    private IStartupRegistrationService? _startupRegistrationService;
    private SettingsSaveTransaction? _settingsSaveTransaction;
    private StartupOptions _startupOptions = StartupOptions.Parse([]);
    private PrivilegeMode _currentPrivilegeMode;
    private NotificationMessage? _pendingStartupNotification;
    private bool _restartAsAdministratorInProgress;
    private bool _settingsWindowOpening;

    /// <summary>
    /// 启动分支按固定顺序处理，每个 Handle* 返回 true 表示当前进程已处理完毕并将退出：
    /// 计划任务维护 → 管理员启动账号预检 → 账号一致性校验 → 单实例协调 → UAC 重启请求。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        InitializeStartupState(e.Args);

        if (HandleStartupTaskMaintenance() ||
            HandleElevationOriginPrecheck() ||
            HandleElevationOriginMismatch() ||
            HandleSingleInstanceCoordination() ||
            HandleAdministratorRestartRequest())
        {
            return;
        }

        ComposeServices();
        StartRuntime();
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

        // 兜底：若退出时仍处于录音暂停了媒体的状态，恢复播放，避免媒体停在暂停态。
        try
        {
            _mediaPlaybackPauseService?.ResumeAllAsync().Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger?.Warn($"退出时恢复媒体播放失败：{ex.Message}");
        }

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

    private void InitializeStartupState(string[] args)
    {
        _startupOptions = StartupOptions.Parse(args);
        _logger = new LocalLogService(AppPaths.LogsDirectory);
        _elevationService = new ElevationService();
        _currentPrivilegeMode = _elevationService.IsRunningAsAdministrator()
            ? PrivilegeMode.Administrator
            : PrivilegeMode.Standard;

        _logger.Info(
            $"启动参数：admin={_startupOptions.RequestAdministrator}, autoStart={_startupOptions.IsAutoStart}, elevationApplied={_startupOptions.ElevationApplied}, startupTaskMaintenance={_startupOptions.StartupTaskMaintenance}, forwardedArgs={FormatForwardedArgs(_startupOptions.ForwardedArgs)}");
        _logger.Info($"当前运行模式：{_currentPrivilegeMode.ToDisplayText()}");
    }

    private bool HandleStartupTaskMaintenance()
    {
        if (_startupOptions.StartupTaskMaintenance == StartupTaskMaintenanceAction.None)
        {
            return false;
        }

        ExecuteStartupTaskMaintenance();
        return true;
    }

    private bool HandleElevationOriginPrecheck()
    {
        if (!_startupOptions.IsElevationOriginCheck)
        {
            return false;
        }

        var originMatches = _startupOptions.IsElevationOriginCurrent(ResolveCurrentUserSid());
        _logger!.Info($"管理员启动账号预检：matches={originMatches}");
        Shutdown(originMatches ? 0 : StartupOptions.ElevationOriginMismatchExitCode);
        return true;
    }

    private bool HandleElevationOriginMismatch()
    {
        if (_startupOptions.IsElevationOriginCurrent(ResolveCurrentUserSid()))
        {
            return false;
        }

        const string message =
            "管理员模式必须由当前 Windows 账号本人确认，不能在 UAC 中改用另一个管理员账号。";
        _logger!.Error(message);
        System.Windows.MessageBox.Show(
            message,
            AppInfo.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(1);
        return true;
    }

    private bool HandleSingleInstanceCoordination()
    {
        var instanceRuntimeContext = InstanceRuntimeContext.CreateForCurrentUser();
        _singleInstanceCoordinator = new SingleInstanceCoordinator(instanceRuntimeContext, _logger!);
        var instanceStartupResult = _singleInstanceCoordinator.CoordinateStartup(_currentPrivilegeMode, _startupOptions);
        if (instanceStartupResult.ShouldContinueStartup)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(instanceStartupResult.ExitMessage))
        {
            System.Windows.MessageBox.Show(
                instanceStartupResult.ExitMessage,
                AppInfo.Title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        Shutdown();
        return true;
    }

    private bool HandleAdministratorRestartRequest()
    {
        if (!_startupOptions.ShouldRestartAsAdministrator(_currentPrivilegeMode == PrivilegeMode.Administrator))
        {
            return false;
        }

        var elevationResult = _elevationService!.RestartAsAdministrator();
        if (elevationResult.WasStarted)
        {
            _logger!.Info("已请求以管理员模式重启当前应用。");
            Shutdown();
            return true;
        }

        if (elevationResult.WasCanceled)
        {
            _logger!.Warn($"管理员模式启动已取消：{elevationResult.Message}");
            _pendingStartupNotification = new NotificationMessage(
                AppInfo.Title,
                "管理员模式启动已取消，当前将继续以普通权限运行。",
                ToolTipIcon.Warning);
        }
        else if (elevationResult.WasFailed)
        {
            _logger!.Error($"请求管理员模式启动失败：{elevationResult.Message}");
            _pendingStartupNotification = new NotificationMessage(
                AppInfo.Title,
                $"请求管理员模式启动失败：{elevationResult.Message}",
                ToolTipIcon.Error);
        }

        return false;
    }

    private void ComposeServices()
    {
        _startupRegistrationService = new StartupRegistrationService(
            new WindowsStartupRegistrationPlatform());

        _settingsService = new SettingsService(AppPaths.SettingsFilePath, _logger!);
        _settingsService.Load();

        _notificationService = new NotificationService();
        _keyboardEventSource = new LowLevelKeyboardEventSource(_logger!);
        _hotkeyManager = new LowLevelKeyboardHotkeyManager(_keyboardEventSource, _logger!);
        _audioCaptureService = new WaveInAudioCaptureService(_settingsService, _logger!);
        _modelProvisioningService = new ModelProvisioningService(_settingsService, _logger!);
        _punctuationModelProvisioningService = new PunctuationModelProvisioningService(_logger!);
        _asrEngine = new SherpaFunAsrNanoEngine(_modelProvisioningService, _settingsService, _logger!);
        _streamingAsrEngine = new SherpaStreamingParaformerEngine(_modelProvisioningService, _settingsService, _logger!);
        _punctuationService = new SherpaOfflinePunctuationService(_logger!);
        _foregroundContextService = new ForegroundContextService(_logger!);
        _textInsertionService = new TextInsertionService(_settingsService, _foregroundContextService, _logger!);
        _postProcessingRuleRepository = new PostProcessingRuleRepository(AppPaths.PostProcessingUserRulesPath, _logger!);
        _postProcessingRuleFactory = new PostProcessingRuleFactory(_logger!);
        _postProcessingService = new PostProcessingService(_postProcessingRuleRepository, _postProcessingRuleFactory, _logger!);
        _statusOverlayService = new StatusOverlayService();
        _dictationOverlayController = new DictationOverlayController(_statusOverlayService);
        _mediaPlaybackPauseService = new MediaPlaybackPauseService(_logger!);
        _settingsSaveTransaction = new SettingsSaveTransaction(
            _settingsService,
            _postProcessingRuleRepository,
            _startupRegistrationService,
            _logger!);
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
            _logger!);

        _trayIconService = new TrayIconService(_notificationService, _logger!, _currentPrivilegeMode);
        _trayIconService.SettingsRequested += (_, _) => OpenSettingsWindow();
        _trayIconService.ModelDownloadRequested += (_, _) => SafeFireAndForget(() => _coordinator.RedownloadModelAsync());
        _trayIconService.ToggleRecordingRequested += (_, _) => SafeFireAndForget(() => _coordinator.ToggleRecordingAsync());
        _trayIconService.RestartAsAdministratorRequested += (_, _) => RestartAsAdministratorFromTray();
        _trayIconService.ExitRequested += (_, _) => Shutdown();

        _coordinator.StateChanged += (_, status) =>
            _ = Dispatcher.InvokeAsync(() =>
            {
                _trayIconService.SetStatus(status.OverlayText);
                _dictationOverlayController.Update(status);
            });

        _coordinator.StateChanged += (_, status) =>
            _mediaPlaybackPauseService.OnDictationStateChanged(status);

        _hotkeyManager.Pressed += (_, _) => SafeFireAndForget(() => _coordinator.BeginRecordingAsync());
        _hotkeyManager.Released += (_, _) => SafeFireAndForget(() => _coordinator.FinalizeRecordingAfterHotkeyReleaseAsync());

        _settingsService.SettingsChanged += (_, changed) =>
        {
            _hotkeyManager.UpdateGesture(changed.Current.Hotkey);
            _ = _coordinator.EnsureModelReadyAsync(downloadIfMissing: false, reinitialize: true);
            _ = _coordinator.EnsurePunctuationReadyAsync(
                downloadIfMissing: changed.Current.AutoDownloadModel,
                reinitialize: true);
        };
    }

    private void StartRuntime()
    {
        _hotkeyManager!.Start(_settingsService!.Current.Hotkey);

        _ = _coordinator!.EnsureModelReadyAsync(downloadIfMissing: _settingsService.Current.AutoDownloadModel);
        _ = _coordinator.EnsurePunctuationReadyAsync(downloadIfMissing: _settingsService.Current.AutoDownloadModel);
        _trayIconService!.SetStatus("就绪");
        _dictationOverlayController!.Update(new DictationStatus
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
            _settingsSaveTransaction is null ||
            _startupRegistrationService is null)
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_settingsWindow is not null && _settingsWindow.IsLoaded)
            {
                ShowAndActivateToForeground(_settingsWindow);
                return;
            }

            // 防重入：后台准备数据期间重复的托盘双击/菜单点击直接忽略，
            // 窗口会在数据就绪后自动出现。
            if (_settingsWindowOpening)
            {
                return;
            }

            _settingsWindowOpening = true;
            SafeFireAndForget(CreateAndShowSettingsWindowAsync);
        });
    }

    private async Task CreateAndShowSettingsWindowAsync()
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            // GetState 会同步 spawn schtasks.exe 并 WaitForExit，GetInputDevices 首次调用
            // 要初始化 winmm，都可能耗时数秒；放后台执行避免阻塞 UI 线程（冻结期间
            // 热键、托盘、悬浮窗全部无响应）。
            var (startupRegistrationMode, startupRegistrationErrorMessage, devices) = await Task.Run(() =>
            {
                StartupRegistrationMode? mode = null;
                string? errorMessage = null;
                try
                {
                    var startupRegistrationState = _startupRegistrationService!.GetState();
                    mode = startupRegistrationState.Mode;
                    errorMessage = startupRegistrationState.Message;
                }
                catch (Exception ex)
                {
                    _logger!.Error("读取登录自启动设置失败。", ex);
                    errorMessage = $"无法读取登录自启动状态，本次保存不会更改该设置：{ex.Message}";
                }

                return (mode, errorMessage, _audioCaptureService!.GetInputDevices());
            });

            var dataReadyMilliseconds = stopwatch.ElapsedMilliseconds;

            _settingsWindow = new SettingsWindow(
                _settingsService!.Current,
                devices,
                _hotkeyManager!,
                _keyboardEventSource!,
                _logger!,
                _postProcessingRuleRepository!,
                _postProcessingService!,
                _currentPrivilegeMode.ToDisplayText(),
                startupRegistrationMode,
                startupRegistrationErrorMessage);

            _settingsWindow.SettingsSaveRequested += async (_, request) =>
            {
                if (request.StartupRegistrationMode == StartupRegistrationMode.Administrator)
                {
                    TryEnsureAdministratorDirectoryTrusted();
                }

                // 事务内部会 spawn schtasks.exe 并同步 WaitForExit，放后台执行避免卡住 UI；
                // 失败异常经由返回的 Task 回传给设置窗统一弹错，错误处理语义不变。
                await Task.Run(() => _settingsSaveTransaction!.Execute(
                    request.Settings,
                    request.PostProcessingConfig,
                    request.StartupRegistrationMode));
            };

            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _logger!.Info(
                $"设置窗口打开：数据准备 {dataReadyMilliseconds}ms，创建 {stopwatch.ElapsedMilliseconds - dataReadyMilliseconds}ms");
            ShowAndActivateToForeground(_settingsWindow);
        }
        finally
        {
            _settingsWindowOpening = false;
        }
    }

    /// <summary>
    /// 从托盘唤出窗口时进程通常不持有前台权限，直接 Activate 可能被系统拒绝而停在后台。
    /// 先以 Topmost 显示窗口（置顶层不受前台权限限制，保证首次呈现即在最前），
    /// 待窗口完成呈现（Loaded 优先级）后再撤销 Topmost 并接管焦点——紧跟 Show
    /// 同步撤销在窗口尚未呈现时不生效。
    /// </summary>
    private static void ShowAndActivateToForeground(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Topmost = true;
        window.Show();
        _ = window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!window.IsLoaded)
            {
                return;
            }

            window.Topmost = false;
            window.Activate();
            window.Focus();
        }));
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

        // 防重入：上一次重启请求还在等待启动包装进程退出时，忽略重复的菜单点击。
        if (_restartAsAdministratorInProgress)
        {
            return;
        }

        TryEnsureAdministratorDirectoryTrusted();

        // ElevationService.RestartAsAdministrator 内部最长同步等待启动包装进程 30 秒，
        // 放后台执行避免托盘点击卡住 UI 线程；结果处理在 await 之后回到 UI 线程。
        _restartAsAdministratorInProgress = true;
        SafeFireAndForget(RestartAsAdministratorOnBackgroundAsync);
    }

    private async Task RestartAsAdministratorOnBackgroundAsync()
    {
        try
        {
            var result = await Task.Run(() => _elevationService!.RestartAsAdministrator());
            if (result.WasStarted)
            {
                _logger!.Info("托盘已触发管理员模式重启。");
                Shutdown();
                return;
            }

            if (result.WasCanceled)
            {
                _logger!.Warn($"托盘管理员模式启动已取消：{result.Message}");
                _notificationService!.Warn(AppInfo.Title, "管理员模式启动已取消，当前实例将继续运行。");
                return;
            }

            if (result.WasFailed)
            {
                _logger!.Error($"托盘请求管理员模式启动失败：{result.Message}");
                _notificationService!.Error(AppInfo.Title, $"请求管理员模式启动失败：{result.Message}");
            }
        }
        finally
        {
            _restartAsAdministratorInProgress = false;
        }
    }

    /// <summary>
    /// 提权前的前置修复步骤：仅当校验失败的原因确定属于"应用安装目录自身的 ACL/属主问题"
    /// 时才提议修复，用户确认后单独提权 icacls.exe 修复（绝不提权应用自己的 exe/cmd 包装
    /// 文件，理由见 <see cref="WindowsStartupAclRepairService"/> 顶部说明）。修复成功与否
    /// 都不在这里分支——调用方随后仍会走既有的 <c>RestartAsAdministrator</c>/<c>SetMode</c>，
    /// 那里对未改动的 <see cref="WindowsStartupRegistrationSecurityValidator.Validate"/> 的
    /// 重新调用才是唯一权威判定。刻意不接入 <c>--admin</c> 命令行路径和计划任务维护的内部
    /// 二次校验——那两处是无人值守场景，不应该弹确认对话框。
    /// </summary>
    private void TryEnsureAdministratorDirectoryTrusted()
    {
        if (_logger is null)
        {
            return;
        }

        var executablePath = Environment.ProcessPath;
        var userSid = ResolveCurrentUserSid();
        if (string.IsNullOrWhiteSpace(executablePath) || string.IsNullOrWhiteSpace(userSid))
        {
            return;
        }

        string? directory;
        bool isRepairable;
        try
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(executablePath));
            isRepairable = directory is not null &&
                WindowsStartupRegistrationSecurityValidator.IsRepairableAclFailure(executablePath, userSid);
        }
        catch (Exception ex)
        {
            _logger.Warn($"检测安装目录权限是否可自动修复时失败：{ex.Message}");
            return;
        }

        if (!isRepairable || directory is null)
        {
            return;
        }

        var confirmed = System.Windows.MessageBox.Show(
            $"检测到安装目录允许当前用户修改，需要额外一次管理员确认来修复权限（仅限本程序目录）：\n{directory}\n是否现在修复？",
            AppInfo.Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
        if (!confirmed)
        {
            return;
        }

        var repairResult = new WindowsStartupAclRepairService().Repair(directory, userSid);
        if (repairResult.WasFailed)
        {
            _logger.Error($"安装目录权限修复失败：{repairResult.Message}");
            System.Windows.MessageBox.Show(
                $"安装目录权限修复失败，接下来的操作大概率仍会报告原有的权限错误：\n{repairResult.Message}",
                AppInfo.Title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        else if (repairResult.WasCanceled)
        {
            _logger.Warn($"安装目录权限修复已取消：{repairResult.Message}");
        }
        else
        {
            _logger.Info("安装目录权限修复完成。");
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

    /// <summary>
    /// 收敛 async void 事件 lambda 的兜底异常防护：后台链路深处逃逸的异常
    /// 不再经 async void 抛到 SyncContext 崩进程，而是记日志后吞掉。
    /// </summary>
    private async void SafeFireAndForget(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger?.Error("异步事件处理发生未处理异常。", ex);
        }
    }

    private static string FormatForwardedArgs(IReadOnlyList<string> args) =>
        args.Count == 0 ? "[]" : $"[{string.Join(", ", args)}]";
}
