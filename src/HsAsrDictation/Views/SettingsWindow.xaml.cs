using System.Windows;
using System.Windows.Input;
using HsAsrDictation.Audio;
using HsAsrDictation.Hotkeys;
using HsAsrDictation.PostProcessing.Abstractions;
using HsAsrDictation.PostProcessing.Engine;
using HsAsrDictation.PostProcessing.Validation;
using HsAsrDictation.Settings;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace HsAsrDictation.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsWindowViewModel _viewModel;
    private readonly IHotkeyManager _hotkeyManager;
    private readonly IPostProcessingRuleRepository _postProcessingRuleRepository;
    private readonly IPostProcessingService _postProcessingService;
    private readonly HotkeyGesture _runtimeHotkey;
    private HotkeyGesture? _captureStartingHotkey;
    private bool _hotkeySuspended;

    public SettingsWindow(
        AppSettings currentSettings,
        IReadOnlyList<AudioDeviceInfo> devices,
        IHotkeyManager hotkeyManager,
        IPostProcessingRuleRepository postProcessingRuleRepository,
        IPostProcessingService postProcessingService)
    {
        InitializeComponent();
        _hotkeyManager = hotkeyManager;
        _postProcessingRuleRepository = postProcessingRuleRepository;
        _postProcessingService = postProcessingService;
        _runtimeHotkey = currentSettings.Hotkey.CreateCopy();
        _viewModel = new SettingsWindowViewModel(
            currentSettings,
            devices,
            _postProcessingRuleRepository.Load());
        DataContext = _viewModel;
    }

    public event EventHandler<AppSettings>? SettingsSaved;

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
        HotkeyCaptureTextBox.SelectAll();
        HotkeyCaptureTextBox.Focus();
        Keyboard.Focus(HotkeyCaptureTextBox);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_viewModel.IsCapturingHotkey)
        {
            return;
        }

        var resolvedKey = HotkeyCaptureParser.ResolveKey(e.Key, e.SystemKey);
        var modifiers = HotkeyCaptureParser.ToHotkeyModifiers(Keyboard.Modifiers);

        if (resolvedKey == Key.Escape)
        {
            e.Handled = true;
            CancelHotkeyCapture();
            return;
        }

        if (HotkeyCaptureParser.TryCreateGesture(
                e.Key,
                e.SystemKey,
                Keyboard.Modifiers,
                out var gesture,
                out var failureReason))
        {
            _viewModel.SetCapturedHotkey(gesture!);
            _captureStartingHotkey = null;
            ReleaseSuspensionIfNoPendingHotkey();
            e.Handled = true;
            return;
        }

        if (failureReason == HotkeyCaptureFailureReason.MissingPrimaryKey && modifiers != HotkeyModifiers.None)
        {
            _viewModel.ShowPressedModifiers(modifiers);
        }
        else if (failureReason == HotkeyCaptureFailureReason.MissingModifier && !HotkeyCaptureParser.IsModifierKey(resolvedKey))
        {
            _viewModel.ShowCaptureGuidance("请至少按住一个修饰键后，再按主键。");
        }
        else if (failureReason == HotkeyCaptureFailureReason.InvalidPrimaryKey)
        {
            _viewModel.ShowCaptureGuidance("该按键不能作为热键主键，请换一个非修饰键。");
        }

        e.Handled = true;
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_viewModel.IsCapturingHotkey)
        {
            return;
        }

        var modifiers = HotkeyCaptureParser.ToHotkeyModifiers(Keyboard.Modifiers);
        if (modifiers == HotkeyModifiers.None)
        {
            _viewModel.ShowCaptureGuidance("请按下组合键，Esc 取消。");
        }
        else
        {
            _viewModel.ShowPressedModifiers(modifiers);
        }

        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
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
}
