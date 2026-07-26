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

    public void Load()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);

        if (!File.Exists(_settingsPath))
        {
            Save(AppSettings.CreateDefault());
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
        AtomicFileWriter.WriteAllText(
            _settingsPath,
            JsonSerializer.Serialize(normalizedSettings, _serializerOptions));
        Current = normalizedSettings;
        _logger.Info($"设置已保存：{_settingsPath}");
    }
}
