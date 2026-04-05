using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using HsAsrDictation.Audio;
using HsAsrDictation.Hotkeys;
using HsAsrDictation.PostProcessing.Models;
using HsAsrDictation.Settings;

namespace HsAsrDictation.Views;

public sealed class SettingsWindowViewModel : INotifyPropertyChanged
{
    private const string ActiveHotkeyCapturePrompt = "请按下组合键，Esc 取消。";

    private readonly HotkeyGesture _runtimeHotkey;
    private HotkeyGesture _candidateHotkey;
    private string _hotkeyCapturePrompt = string.Empty;
    private bool _isCapturingHotkey;

    public SettingsWindowViewModel(
        AppSettings settings,
        IReadOnlyList<AudioDeviceInfo> devices,
        PostProcessingConfig postProcessingConfig,
        HotkeyGesture? runtimeHotkey = null)
    {
        var effectiveHotkey = (runtimeHotkey ?? settings.Hotkey).CreateCopy();
        Devices = new ObservableCollection<AudioDeviceInfo>(devices);
        RecognitionModes = new ObservableCollection<RecognitionModeOption>(BuildRecognitionModes());
        PostProcessing = new PostProcessingRulesViewModel(postProcessingConfig);
        _runtimeHotkey = effectiveHotkey;
        _candidateHotkey = effectiveHotkey.CreateCopy();

        SelectedDeviceName = settings.PreferredInputDeviceName;
        OfflineModelRootPath = settings.OfflineModelRootPath;
        StreamingModelRootPath = settings.StreamingModelRootPath;
        AllowClipboardFallback = settings.AllowClipboardFallback;
        AutoDownloadModel = settings.AutoDownloadModel;
        EnablePunctuation = settings.EnablePunctuation;
        EnableStreamingPreview = settings.EnableStreamingPreview;
        SelectedRecognitionMode = RecognitionModes.First(x => x.Mode == settings.RecognitionMode);
        _hotkeyCapturePrompt = BuildIdleHotkeyPrompt();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AudioDeviceInfo> Devices { get; }

    public ObservableCollection<RecognitionModeOption> RecognitionModes { get; }

    public PostProcessingRulesViewModel PostProcessing { get; }

    public string? SelectedDeviceName { get; set; }

    public string OfflineModelRootPath { get; set; }

    public string StreamingModelRootPath { get; set; }

    public bool AllowClipboardFallback { get; set; }

    public bool AutoDownloadModel { get; set; }

    public bool EnablePunctuation { get; set; }

    public bool EnableStreamingPreview { get; set; }

    public RecognitionModeOption SelectedRecognitionMode { get; set; } = null!;

    public HotkeyGesture CandidateHotkey
    {
        get => _candidateHotkey;
        private set
        {
            if (_candidateHotkey.IsEquivalentTo(value))
            {
                return;
            }

            _candidateHotkey = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HotkeyDisplayText));
        }
    }

    public string HotkeyDisplayText => HotkeyDisplayPresentation.BuildDisplayText(
        _runtimeHotkey.ToDisplayText(),
        CandidateHotkey.ToDisplayText(),
        HasPendingHotkey);

    public string HotkeyCapturePrompt
    {
        get => _hotkeyCapturePrompt;
        private set => SetProperty(ref _hotkeyCapturePrompt, value);
    }

    public bool IsCapturingHotkey
    {
        get => _isCapturingHotkey;
        private set
        {
            if (SetProperty(ref _isCapturingHotkey, value))
            {
                OnPropertyChanged(nameof(HotkeyCaptureButtonText));
            }
        }
    }

    public string HotkeyCaptureButtonText => IsCapturingHotkey ? "取消录入" : "开始录入";

    public AppSettings ToSettings()
    {
        return new AppSettings
        {
            PreferredInputDeviceName = string.IsNullOrWhiteSpace(SelectedDeviceName)
                ? null
                : SelectedDeviceName,
            ModelRootPath = ResolveModelRootPath(),
            OfflineModelRootPath = OfflineModelRootPath,
            StreamingModelRootPath = StreamingModelRootPath,
            AllowClipboardFallback = AllowClipboardFallback,
            AutoDownloadModel = AutoDownloadModel,
            EnablePunctuation = EnablePunctuation,
            EnablePostProcessingRules = PostProcessing.IsRuleSystemEnabled,
            RecognitionMode = SelectedRecognitionMode.Mode,
            EnableStreamingPreview = EnableStreamingPreview,
            Hotkey = CandidateHotkey.CreateCopy()
        };
    }

    public void BeginHotkeyCapture()
    {
        IsCapturingHotkey = true;
        HotkeyCapturePrompt = ActiveHotkeyCapturePrompt;
    }

    public void CancelHotkeyCapture(HotkeyGesture restoredHotkey, bool keepPendingHotkey)
    {
        CandidateHotkey = restoredHotkey.CreateCopy();
        IsCapturingHotkey = false;
        HotkeyCapturePrompt = HotkeyDisplayPresentation.BuildCanceledPrompt(
            _runtimeHotkey.ToDisplayText(),
            CandidateHotkey.ToDisplayText(),
            keepPendingHotkey);
    }

    public void SetCapturedHotkey(HotkeyGesture hotkey)
    {
        CandidateHotkey = hotkey.CreateCopy();
        IsCapturingHotkey = false;
        HotkeyCapturePrompt = HotkeyDisplayPresentation.BuildCapturedPrompt(
            _runtimeHotkey.ToDisplayText(),
            CandidateHotkey.ToDisplayText());
    }

    public void ShowPressedModifiers(HotkeyModifiers modifiers)
    {
        HotkeyCapturePrompt = $"已按下 {HotkeyCaptureEvaluator.FormatModifierText(modifiers)}，继续按主键。";
    }

    public void ShowCaptureGuidance(string prompt)
    {
        HotkeyCapturePrompt = prompt;
    }

    private bool HasPendingHotkey => !_runtimeHotkey.IsEquivalentTo(CandidateHotkey);

    private string BuildIdleHotkeyPrompt() =>
        HasPendingHotkey
            ? HotkeyDisplayPresentation.BuildPendingPrompt(
                _runtimeHotkey.ToDisplayText(),
                CandidateHotkey.ToDisplayText())
            : HotkeyDisplayPresentation.BuildIdlePrompt(_runtimeHotkey.ToDisplayText());

    private static IEnumerable<RecognitionModeOption> BuildRecognitionModes()
    {
        yield return new RecognitionModeOption(RecognitionMode.Hybrid, "混合模式");
        yield return new RecognitionModeOption(RecognitionMode.StreamingOnly, "纯流式");
        yield return new RecognitionModeOption(RecognitionMode.NonStreaming, "非流式");
    }

    private string ResolveModelRootPath()
    {
        if (!string.IsNullOrWhiteSpace(OfflineModelRootPath) &&
            !string.IsNullOrWhiteSpace(StreamingModelRootPath))
        {
            var offlineParent = System.IO.Path.GetDirectoryName(OfflineModelRootPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            var streamingParent = System.IO.Path.GetDirectoryName(StreamingModelRootPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

            if (!string.IsNullOrWhiteSpace(offlineParent) &&
                string.Equals(offlineParent, streamingParent, StringComparison.OrdinalIgnoreCase))
            {
                return offlineParent;
            }
        }

        return Services.AppPaths.DefaultModelRootPath;
    }

    public sealed record RecognitionModeOption(RecognitionMode Mode, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
