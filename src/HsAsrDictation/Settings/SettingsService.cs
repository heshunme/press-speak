using System.IO;
using System.Text.Json;
using HsAsrDictation.Logging;

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
            Current = (JsonSerializer.Deserialize<AppSettings>(json, _serializerOptions) ?? AppSettings.CreateDefault())
                .Normalize();
        }
        catch (Exception ex)
        {
            _logger.Error("加载设置失败，已回退默认设置。", ex);
            Current = AppSettings.CreateDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        Current = settings.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(Current, _serializerOptions));
        _logger.Info($"设置已保存：{_settingsPath}");
    }
}
