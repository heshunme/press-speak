using System.Windows;
using HsAsrDictation.Audio;
using HsAsrDictation.Settings;

namespace HsAsrDictation.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsWindowViewModel _viewModel;

    public SettingsWindow(AppSettings currentSettings, IReadOnlyList<AudioDeviceInfo> devices)
    {
        InitializeComponent();
        _viewModel = new SettingsWindowViewModel(currentSettings, devices);
        DataContext = _viewModel;
    }

    public event EventHandler<AppSettings>? SettingsSaved;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
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
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
