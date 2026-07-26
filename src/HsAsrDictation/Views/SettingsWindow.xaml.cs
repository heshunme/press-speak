using System.Windows;
using System.Windows.Input;
using HsAsrDictation.Audio;
using HsAsrDictation.Hotkeys;
using HsAsrDictation.Logging;
using HsAsrDictation.PostProcessing.Abstractions;
using HsAsrDictation.PostProcessing.Engine;
using HsAsrDictation.PostProcessing.Models;
using HsAsrDictation.PostProcessing.Validation;
using HsAsrDictation.Services;
using HsAsrDictation.Settings;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace HsAsrDictation.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsWindowViewModel _viewModel;
    private readonly IHotkeyManager _hotkeyManager;
    private readonly LowLevelKeyboardEventSource _keyboardEventSource;
    private readonly LocalLogService _logger;
    private readonly IPostProcessingRuleRepository _postProcessingRuleRepository;
    private readonly IPostProcessingService _postProcessingService;
    private readonly HotkeyPressedState _capturePressedState = new();
    private readonly List<HotkeyPhysicalKey> _captureSequence = [];
    private readonly HashSet<HotkeyPhysicalKey> _captureSequenceSet = [];
    private readonly HotkeyGesture _runtimeHotkey;
    private HotkeyGesture? _captureStartingHotkey;
    private bool _hotkeySuspended;

    public SettingsWindow(
        AppSettings currentSettings,
        IReadOnlyList<AudioDeviceInfo> devices,
        IHotkeyManager hotkeyManager,
        LowLevelKeyboardEventSource keyboardEventSource,
        LocalLogService logger,
        IPostProcessingRuleRepository postProcessingRuleRepository,
        IPostProcessingService postProcessingService,
        string currentPrivilegeModeText,
        StartupRegistrationMode? startupRegistrationMode,
        string? startupRegistrationErrorMessage = null)
    {
        InitializeComponent();
        _hotkeyManager = hotkeyManager;
        _keyboardEventSource = keyboardEventSource;
        _logger = logger;
        _postProcessingRuleRepository = postProcessingRuleRepository;
        _postProcessingService = postProcessingService;
        _keyboardEventSource.KeyEvent += OnKeyboardEvent;
        _runtimeHotkey = _hotkeyManager.CurrentGesture.CreateCopy();
        _viewModel = new SettingsWindowViewModel(
            currentSettings,
            devices,
            _postProcessingRuleRepository.Load(),
            startupRegistrationMode,
            _hotkeyManager.CurrentGesture,
            currentPrivilegeModeText,
            startupRegistrationErrorMessage);
        DataContext = _viewModel;
    }

    public event EventHandler<SettingsSaveRequestedEventArgs>? SettingsSaveRequested;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsCapturingHotkey)
        {
            System.Windows.MessageBox.Show(this, "请先完成或取消热键录入。", "HsAsrDictation");
            return;
        }

        if (string.IsNullOrWhiteSpace(_viewModel.OfflineModelRootPath))
        {
            System.Windows.MessageBox.Show(this, "离线模型目录不能为空。", "HsAsrDictation");
            return;
        }

        if (string.IsNullOrWhiteSpace(_viewModel.StreamingModelRootPath))
        {
            System.Windows.MessageBox.Show(this, "流式模型目录不能为空。", "HsAsrDictation");
            return;
        }

        if (!int.TryParse(_viewModel.MaxRecordingDurationSecondsText?.Trim(), out var maxRecordingDurationSeconds))
        {
            System.Windows.MessageBox.Show(this, "单次录音上限必须是整数秒。", "HsAsrDictation");
            return;
        }

        if (maxRecordingDurationSeconds < AppSettings.MinMaxRecordingDurationSeconds ||
            maxRecordingDurationSeconds > AppSettings.MaxMaxRecordingDurationSeconds)
        {
            System.Windows.MessageBox.Show(
                this,
                $"单次录音上限必须在 {AppSettings.MinMaxRecordingDurationSeconds} 到 {AppSettings.MaxMaxRecordingDurationSeconds} 秒之间。",
                "HsAsrDictation");
            return;
        }

        if (!int.TryParse(_viewModel.HotkeyReleaseTailDurationMillisecondsText?.Trim(), out var hotkeyReleaseTailDurationMilliseconds))
        {
            System.Windows.MessageBox.Show(this, "松键尾录延迟必须是整数毫秒。", "HsAsrDictation");
            return;
        }

        if (hotkeyReleaseTailDurationMilliseconds < AppSettings.MinHotkeyReleaseTailDurationMilliseconds ||
            hotkeyReleaseTailDurationMilliseconds > AppSettings.MaxHotkeyReleaseTailDurationMilliseconds)
        {
            System.Windows.MessageBox.Show(
                this,
                $"松键尾录延迟必须在 {AppSettings.MinHotkeyReleaseTailDurationMilliseconds} 到 {AppSettings.MaxHotkeyReleaseTailDurationMilliseconds} 毫秒之间。",
                "HsAsrDictation");
            return;
        }

        var config = _viewModel.PostProcessing.BuildConfig();
        var (ok, error) = RuleValidator.ValidateConfig(config);
        if (!ok)
        {
            System.Windows.MessageBox.Show(this, error, "HsAsrDictation");
            return;
        }

        var updatedSettings = _viewModel.ToSettings(
            maxRecordingDurationSeconds,
            hotkeyReleaseTailDurationMilliseconds);
        try
        {
            SettingsSaveRequested?.Invoke(
                this,
                new SettingsSaveRequestedEventArgs(
                    updatedSettings,
                    config,
                    _viewModel.DesiredStartupRegistrationMode));
            ResumeRuntimeHotkeyIfNeeded();
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"保存设置失败：{ex.Message}", "HsAsrDictation");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleHotkeyCapture_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsCapturingHotkey)
        {
            CancelHotkeyCapture();
            return;
        }

        _captureStartingHotkey = _viewModel.CandidateHotkey.CreateCopy();
        SuspendRuntimeHotkeyIfNeeded();
        _viewModel.BeginHotkeyCapture();
        ResetCaptureState();
        HotkeyCaptureTextBox.SelectAll();
        HotkeyCaptureTextBox.Focus();
        Keyboard.Focus(HotkeyCaptureTextBox);
        _logger.Info($"开始热键录入：runtime={_runtimeHotkey.ToDisplayText()}");
    }

    protected override void OnClosed(EventArgs e)
    {
        _keyboardEventSource.KeyEvent -= OnKeyboardEvent;
        ResumeRuntimeHotkeyIfNeeded();
        base.OnClosed(e);
    }

    private void CancelHotkeyCapture()
    {
        var restoredHotkey = _captureStartingHotkey ?? _viewModel.CandidateHotkey;
        _captureStartingHotkey = null;

        _viewModel.CancelHotkeyCapture(
            restoredHotkey,
            keepPendingHotkey: !_runtimeHotkey.IsEquivalentTo(restoredHotkey));

        ResetCaptureState();
        _logger.Info($"已取消热键录入：restored={restoredHotkey.ToDisplayText()}");
        ReleaseSuspensionIfNoPendingHotkey();
    }

    private void SuspendRuntimeHotkeyIfNeeded()
    {
        if (_hotkeySuspended)
        {
            return;
        }

        _hotkeyManager.Suspend();
        _hotkeySuspended = true;
    }

    private void ResumeRuntimeHotkeyIfNeeded()
    {
        if (!_hotkeySuspended)
        {
            return;
        }

        _hotkeyManager.Resume();
        _hotkeySuspended = false;
    }

    private void ReleaseSuspensionIfNoPendingHotkey()
    {
        if (_viewModel.IsCapturingHotkey || !_runtimeHotkey.IsEquivalentTo(_viewModel.CandidateHotkey))
        {
            return;
        }

        ResumeRuntimeHotkeyIfNeeded();
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.PostProcessing.AddRule();
    }

    private void DuplicateRule_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.PostProcessing.DuplicateSelectedRule();
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.PostProcessing.DeleteSelectedRule();
    }

    private void MoveRuleUp_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.PostProcessing.MoveSelectedRule(-1);
    }

    private void MoveRuleDown_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.PostProcessing.MoveSelectedRule(1);
    }

    private void ResetRule_Click(object sender, RoutedEventArgs e)
    {
        var selectedRule = _viewModel.PostProcessing.SelectedRule;
        if (selectedRule is null || !selectedRule.IsBuiltIn)
        {
            return;
        }

        try
        {
            _postProcessingRuleRepository.ResetBuiltInOverride(selectedRule.Id);
            _viewModel.PostProcessing.Load(_postProcessingRuleRepository.Load());
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"恢复默认失败：{ex.Message}", "HsAsrDictation");
        }
    }

    private void TestRules_Click(object sender, RoutedEventArgs e)
    {
        var config = _viewModel.PostProcessing.BuildConfig();
        var (ok, error) = RuleValidator.ValidateConfig(config);
        if (!ok)
        {
            System.Windows.MessageBox.Show(this, error, "HsAsrDictation");
            return;
        }

        try
        {
            var result = _postProcessingService.TestProcess(
                config,
                _viewModel.PostProcessing.TestInput,
                new RuleExecutionContext
                {
                    IsPasswordField = false
                });

            _viewModel.PostProcessing.TestOutput = result.Output;
            _viewModel.PostProcessing.TestTrace = result.TraceEntries.Count == 0
                ? (result.UsedFallback ? "已回退原始文本。" : "没有规则命中。")
                : string.Join(Environment.NewLine, result.TraceEntries.Select(entry =>
                    $"{entry.RuleId} | {(entry.Failed ? "FAILED" : entry.Changed ? "CHANGED" : "SKIPPED")} | {entry.Message}"));
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"规则测试失败：{ex.Message}", "HsAsrDictation");
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.IsCapturingHotkey)
        {
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (_viewModel.IsCapturingHotkey)
        {
            e.Handled = true;
        }
    }

    private void OnKeyboardEvent(object? sender, HotkeyEventData keyEvent)
    {
        if (!_viewModel.IsCapturingHotkey)
        {
            return;
        }

        if (keyEvent.IsKeyDown && keyEvent.VirtualKey == 0x1B)
        {
            CancelHotkeyCapture();
            return;
        }

        if (!keyEvent.TryGetPhysicalKey(out var physicalKey))
        {
            return;
        }

        _capturePressedState.Apply(keyEvent);
        if (keyEvent.IsKeyDown && _captureSequenceSet.Add(physicalKey))
        {
            _captureSequence.Add(physicalKey);
        }

        if (!_capturePressedState.IsEmpty)
        {
            _viewModel.ShowCaptureProgress(HotkeyCaptureEvaluator.FormatKeyText(_capturePressedState.PressedKeys));
            return;
        }

        if (HotkeyCaptureParser.TryCreateGesture(
                _captureSequence,
                out var gesture,
                out var failureReason))
        {
            _viewModel.SetCapturedHotkey(gesture!);
            _captureStartingHotkey = null;
            _logger.Info($"热键录入成功：{FormatEventData(keyEvent)} | captured={gesture!.ToDisplayText()}");
            ResetCaptureState();
            ReleaseSuspensionIfNoPendingHotkey();
            return;
        }

        ResetCaptureState();
        if (failureReason == HotkeyCaptureFailureReason.SingleWindowsKeyNotAllowed)
        {
            _logger.Info($"热键录入主键无效：{FormatEventData(keyEvent)}");
            _viewModel.ShowCaptureGuidance("不能将单独的 Win 键设为热键，请加入其他键后重试。");
        }
        else if (failureReason == HotkeyCaptureFailureReason.EscapeReserved)
        {
            _viewModel.ShowCaptureGuidance("Esc 仅用于取消录入，不能保存为热键。");
        }
        else
        {
            _viewModel.ShowCaptureGuidance("请按下要作为热键的按键或组合，松开全部按键后完成录入，Esc 取消。");
        }
    }

    private void ResetCaptureState()
    {
        _capturePressedState.Clear();
        _captureSequence.Clear();
        _captureSequenceSet.Clear();
    }

    private static string FormatEventData(HotkeyEventData keyEvent) =>
        $"vk=0x{keyEvent.VirtualKey:X2}, scan=0x{keyEvent.ScanCode:X2}, extended={keyEvent.IsExtendedKey}, altContext={keyEvent.IsAltContext}, injected={keyEvent.IsInjected}, keyDown={keyEvent.IsKeyDown}";
}

public sealed class SettingsSaveRequestedEventArgs : EventArgs
{
    public SettingsSaveRequestedEventArgs(
        AppSettings settings,
        PostProcessingConfig postProcessingConfig,
        StartupRegistrationMode? startupRegistrationMode)
    {
        Settings = settings;
        PostProcessingConfig = postProcessingConfig;
        StartupRegistrationMode = startupRegistrationMode;
    }

    public AppSettings Settings { get; }

    public PostProcessingConfig PostProcessingConfig { get; }

    public StartupRegistrationMode? StartupRegistrationMode { get; }
}
