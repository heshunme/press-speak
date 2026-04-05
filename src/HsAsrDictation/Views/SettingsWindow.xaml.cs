using System.Windows;
using System.Windows.Input;
using HsAsrDictation.Audio;
using HsAsrDictation.Hotkeys;
using HsAsrDictation.Settings;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace HsAsrDictation.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsWindowViewModel _viewModel;
    private readonly IHotkeyManager _hotkeyManager;
    private readonly HotkeyGesture _runtimeHotkey;
    private HotkeyGesture? _captureStartingHotkey;
    private bool _hotkeySuspended;

    public SettingsWindow(AppSettings currentSettings, IReadOnlyList<AudioDeviceInfo> devices, IHotkeyManager hotkeyManager)
    {
        InitializeComponent();
        _hotkeyManager = hotkeyManager;
        _runtimeHotkey = currentSettings.Hotkey.CreateCopy();
        _viewModel = new SettingsWindowViewModel(currentSettings, devices);
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

        var updatedSettings = _viewModel.ToSettings();
        SettingsSaved?.Invoke(this, updatedSettings);
        ResumeRuntimeHotkeyIfNeeded();
        Close();
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
}
