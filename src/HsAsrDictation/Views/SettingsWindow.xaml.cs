using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using HsAsrDictation.Audio;
using HsAsrDictation.Hotkeys;
using HsAsrDictation.Interop;
using HsAsrDictation.Logging;
using HsAsrDictation.PostProcessing.Abstractions;
using HsAsrDictation.PostProcessing.Engine;
using HsAsrDictation.PostProcessing.Validation;
using HsAsrDictation.Settings;

namespace HsAsrDictation.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsWindowViewModel _viewModel;
    private readonly IHotkeyManager _hotkeyManager;
    private readonly LocalLogService _logger;
    private readonly IPostProcessingRuleRepository _postProcessingRuleRepository;
    private readonly IPostProcessingService _postProcessingService;
    private readonly HotkeyPressedState _capturePressedState = new();
    private readonly HotkeyGesture _runtimeHotkey;
    private HotkeyGesture? _captureStartingHotkey;
    private HwndSource? _hwndSource;
    private bool _hotkeySuspended;

    public SettingsWindow(
        AppSettings currentSettings,
        IReadOnlyList<AudioDeviceInfo> devices,
        IHotkeyManager hotkeyManager,
        LocalLogService logger,
        IPostProcessingRuleRepository postProcessingRuleRepository,
        IPostProcessingService postProcessingService)
    {
        InitializeComponent();
        _hotkeyManager = hotkeyManager;
        _logger = logger;
        _postProcessingRuleRepository = postProcessingRuleRepository;
        _postProcessingService = postProcessingService;
        _runtimeHotkey = _hotkeyManager.CurrentGesture.CreateCopy();
        _viewModel = new SettingsWindowViewModel(
            currentSettings,
            devices,
            _postProcessingRuleRepository.Load(),
            _hotkeyManager.CurrentGesture);
        DataContext = _viewModel;
    }

    public event EventHandler<AppSettings>? SettingsSaved;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        _hwndSource?.AddHook(WindowMessageHook);
    }

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

        var config = _viewModel.PostProcessing.BuildConfig();
        var (ok, error) = RuleValidator.ValidateConfig(config);
        if (!ok)
        {
            System.Windows.MessageBox.Show(this, error, "HsAsrDictation");
            return;
        }

        var updatedSettings = _viewModel.ToSettings();
        try
        {
            _postProcessingRuleRepository.Save(config);
            SettingsSaved?.Invoke(this, updatedSettings);
            ResumeRuntimeHotkeyIfNeeded();
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"保存后处理规则失败：{ex.Message}", "HsAsrDictation");
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
        _capturePressedState.Clear();
        _capturePressedState.SetPressedModifiers(HotkeyCaptureParser.ToHotkeyModifiers(Keyboard.Modifiers));
        HotkeyCaptureTextBox.SelectAll();
        HotkeyCaptureTextBox.Focus();
        Keyboard.Focus(HotkeyCaptureTextBox);
        _logger.Info($"开始热键录入：runtime={_runtimeHotkey.ToDisplayText()}");
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WindowMessageHook);
            _hwndSource = null;
        }

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

    private IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_viewModel.IsCapturingHotkey)
        {
            return IntPtr.Zero;
        }

        if (msg == Win32.WM_SYSCHAR)
        {
            handled = true;
            return IntPtr.Zero;
        }

        if (msg is not (Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN or Win32.WM_KEYUP or Win32.WM_SYSKEYUP))
        {
            return IntPtr.Zero;
        }

        var keyEvent = HotkeyEventTranslator.FromWindowMessage(msg, wParam, lParam);
        _capturePressedState.Apply(keyEvent);
        var modifiers = _capturePressedState.GetPressedModifiers(includeAltContext: keyEvent.IsAltContext);
        var failureReason = HotkeyCaptureFailureReason.None;

        if (keyEvent.IsKeyDown && keyEvent.VirtualKey == 0x1B)
        {
            CancelHotkeyCapture();
            handled = true;
            return IntPtr.Zero;
        }

        if (keyEvent.IsKeyDown &&
            HotkeyCaptureParser.TryCreateGesture(
                keyEvent,
                modifiers,
                out var gesture,
                out failureReason))
        {
            _viewModel.SetCapturedHotkey(gesture!);
            _captureStartingHotkey = null;
            _logger.Info($"热键录入成功：{FormatEventData(keyEvent)} | modifiers={gesture!.Modifiers} | captured={gesture.ToDisplayText()}");
            ReleaseSuspensionIfNoPendingHotkey();
            handled = true;
            return IntPtr.Zero;
        }

        if (!keyEvent.IsKeyDown)
        {
            if (modifiers == HotkeyModifiers.None)
            {
                _viewModel.ShowCaptureGuidance("请按下组合键，Esc 取消。");
            }
            else
            {
                _viewModel.ShowPressedModifiers(modifiers);
            }

            handled = true;
            return IntPtr.Zero;
        }

        if (failureReason == HotkeyCaptureFailureReason.MissingPrimaryKey && modifiers != HotkeyModifiers.None)
        {
            _logger.Info($"热键录入等待主键：{FormatEventData(keyEvent)} | modifiers={modifiers}");
            _viewModel.ShowPressedModifiers(modifiers);
        }
        else if (failureReason == HotkeyCaptureFailureReason.MissingModifier && !keyEvent.IsModifier)
        {
            _logger.Info($"热键录入缺少修饰键：{FormatEventData(keyEvent)}");
            _viewModel.ShowCaptureGuidance("请至少按住一个修饰键后，再按主键。");
        }
        else if (failureReason == HotkeyCaptureFailureReason.InvalidPrimaryKey)
        {
            _logger.Info($"热键录入主键无效：{FormatEventData(keyEvent)}");
            _viewModel.ShowCaptureGuidance("该按键不能作为热键主键，请换一个非修饰键。");
        }

        handled = true;
        return IntPtr.Zero;
    }

    private static string FormatEventData(HotkeyEventData keyEvent) =>
        $"vk=0x{keyEvent.VirtualKey:X2}, scan=0x{keyEvent.ScanCode:X2}, extended={keyEvent.IsExtendedKey}, altContext={keyEvent.IsAltContext}, keyDown={keyEvent.IsKeyDown}";
}
