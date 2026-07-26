using System.IO;
using System.Text.Json;
using HsAsrDictation.Logging;
using HsAsrDictation.Services;

namespace HsAsrDictation.Settings;

public sealed class SettingsService
{
    private readonly string _settingsPath;
    private readonly LocalLogService _logger;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true
    };

    public SettingsService(string settingsPath, LocalLogService logger)
    {
        _settingsPath = settingsPath;
        _logger = logger;
        Current = AppSettings.CreateDefault();
    }

    public AppSettings Current { get; private set; }

    /// <summary>设置保存成功后触发（包括保存失败后的回滚性保存）。订阅者据此刷新热键、模型等运行时状态。</summary>
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    public void Load()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);

        if (!File.Exists(_settingsPath))
        {
            var defaults = AppSettings.CreateDefault().Normalize();
            WriteToDisk(defaults);
            Current = defaults;
            return;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var loadedSettings = JsonSerializer.Deserialize<AppSettings>(json, _serializerOptions) ?? AppSettings.CreateDefault();
            var hotkeyWasInvalid = loadedSettings.Hotkey is null || !loadedSettings.Hotkey.IsValid;
            Current = loadedSettings.Normalize();

            if (hotkeyWasInvalid)
            {
                _logger.Warn("热键配置无效或已过时，已回退为默认热键 Right Alt。");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("加载设置失败，已回退默认设置。", ex);
            Current = AppSettings.CreateDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        var normalizedSettings = settings.Normalize();
        WriteToDisk(normalizedSettings);
        var previousSettings = Current;
        Current = normalizedSettings;
        _logger.Info($"设置已保存：{_settingsPath}");
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(previousSettings, normalizedSettings));
    }

    private void WriteToDisk(AppSettings normalizedSettings)
    {
        AtomicFileWriter.WriteAllText(
            _settingsPath,
            JsonSerializer.Serialize(normalizedSettings, _serializerOptions));
    }
}

public sealed class SettingsChangedEventArgs : EventArgs
{
    public SettingsChangedEventArgs(AppSettings previous, AppSettings current)
    {
        Previous = previous;
        Current = current;
    }

    public AppSettings Previous { get; }

    public AppSettings Current { get; }
}
