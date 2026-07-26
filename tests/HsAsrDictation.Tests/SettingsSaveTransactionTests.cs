using HsAsrDictation.Logging;
using HsAsrDictation.PostProcessing.Abstractions;
using HsAsrDictation.PostProcessing.Models;
using HsAsrDictation.Services;
using HsAsrDictation.Settings;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class SettingsSaveTransactionTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly LocalLogService _logger;
    private readonly SettingsService _settings;
    private readonly FakeRuleRepository _ruleRepository = new();
    private readonly FakeStartupRegistrationService _startupRegistration = new();

    public SettingsSaveTransactionTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "hs-asr-tests",
            $"{nameof(SettingsSaveTransactionTests)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _logger = new LocalLogService(Path.Combine(_tempDirectory, "logs"));
        _settings = new SettingsService(Path.Combine(_tempDirectory, "settings.json"), _logger);
        _settings.Load();
    }

    public void Dispose()
    {
        _logger.Dispose();
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch
        {
        }
    }

    private SettingsSaveTransaction CreateTransaction() =>
        new(_settings, _ruleRepository, _startupRegistration, _logger);

    private static AppSettings NewSettings() => new()
    {
        EnablePunctuation = true
    };

    [Fact]
    public void Execute_SavesRulesAndSettings_WithoutStartupChangeWhenNotRequested()
    {
        CreateTransaction().Execute(NewSettings(), new PostProcessingConfig(), startupRegistrationMode: null);

        Assert.Equal(1, _ruleRepository.SaveCallCount);
        Assert.True(_settings.Current.EnablePunctuation);
        Assert.Empty(_startupRegistration.SetModeCalls);
    }

    [Fact]
    public void Execute_AppliesStartupRegistration_BeforeSavingRulesAndSettings()
    {
        CreateTransaction().Execute(
            NewSettings(),
            new PostProcessingConfig(),
            StartupRegistrationMode.Standard);

        Assert.Equal(new[] { StartupRegistrationMode.Standard }, _startupRegistration.SetModeCalls);
        Assert.Equal(1, _ruleRepository.SaveCallCount);
    }

    [Fact]
    public void Execute_WhenStartupRegistrationFails_ThrowsWithoutTouchingRulesOrSettings()
    {
        _startupRegistration.NextResult = StartupRegistrationChangeResult.Failed("拒绝");

        Assert.Throws<InvalidOperationException>(() =>
            CreateTransaction().Execute(NewSettings(), new PostProcessingConfig(), StartupRegistrationMode.Standard));

        Assert.Equal(0, _ruleRepository.SaveCallCount);
        Assert.False(_settings.Current.EnablePunctuation);
    }

    [Fact]
    public void Execute_WhenRuleSaveFails_RollsBackStartupRegistrationAndThrows()
    {
        _startupRegistration.CurrentMode = StartupRegistrationMode.Disabled;
        _ruleRepository.FailNextSave = true;

        Assert.Throws<InvalidOperationException>(() =>
            CreateTransaction().Execute(NewSettings(), new PostProcessingConfig(), StartupRegistrationMode.Standard));

        // 第一次 SetMode 应用新值；规则保存失败后应回滚为旧值。
        Assert.Equal(
            new[] { StartupRegistrationMode.Standard, StartupRegistrationMode.Disabled },
            _startupRegistration.SetModeCalls);
        Assert.False(_settings.Current.EnablePunctuation);
    }

    [Fact]
    public void Execute_WhenSettingsSaveFails_RollsBackRulesAndThrows()
    {
        // 把 settings.json 变成目录，使设置写入失败。
        var settingsPath = Path.Combine(_tempDirectory, "settings.json");
        File.Delete(settingsPath);
        Directory.CreateDirectory(settingsPath);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateTransaction().Execute(NewSettings(), new PostProcessingConfig(), startupRegistrationMode: null));

        // 规则先成功保存一次，随后回滚为旧配置又保存一次；
        // 设置从未成功保存（Current 未变），不做回滚性保存，因此不产生回滚失败。
        Assert.Equal(2, _ruleRepository.SaveCallCount);
        Assert.Same(_ruleRepository.InitialConfig, _ruleRepository.LastSavedConfig);
        Assert.Contains("已恢复原设置", exception.Message, StringComparison.Ordinal);
    }

    private sealed class FakeRuleRepository : IPostProcessingRuleRepository
    {
        public PostProcessingConfig InitialConfig { get; } = new();

        public PostProcessingConfig? LastSavedConfig { get; private set; }

        public int SaveCallCount { get; private set; }

        public bool FailNextSave { get; set; }

        public PostProcessingConfig Load() => InitialConfig;

        public void Save(PostProcessingConfig config)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("磁盘已满");
            }

            SaveCallCount++;
            LastSavedConfig = config;
        }

        public void ResetBuiltInOverride(string ruleId)
        {
        }
    }

    private sealed class FakeStartupRegistrationService : IStartupRegistrationService
    {
        public StartupRegistrationMode CurrentMode { get; set; } = StartupRegistrationMode.Disabled;

        public StartupRegistrationChangeResult? NextResult { get; set; }

        public List<StartupRegistrationMode> SetModeCalls { get; } = [];

        public StartupRegistrationState GetState() => new()
        {
            Mode = CurrentMode
        };

        public StartupRegistrationMode GetMode() => CurrentMode;

        public StartupRegistrationChangeResult SetMode(StartupRegistrationMode mode)
        {
            if (NextResult is { } result)
            {
                NextResult = null;
                return result;
            }

            SetModeCalls.Add(mode);
            CurrentMode = mode;
            return StartupRegistrationChangeResult.Succeeded();
        }
    }
}
