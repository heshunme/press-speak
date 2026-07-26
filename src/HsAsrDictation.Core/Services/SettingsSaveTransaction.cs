using HsAsrDictation.Logging;
using HsAsrDictation.PostProcessing.Abstractions;
using HsAsrDictation.PostProcessing.Models;
using HsAsrDictation.Settings;

namespace HsAsrDictation.Services;

/// <summary>
/// 把自启动注册、后处理规则、应用设置作为一个整体保存：
/// 顺序为自启动 → 规则 → 设置；任一步失败会逆序回滚之前已保存的部分，
/// 并抛出 <see cref="InvalidOperationException"/> 供设置窗显示。
/// </summary>
public sealed class SettingsSaveTransaction
{
    private readonly SettingsService _settingsService;
    private readonly IPostProcessingRuleRepository _postProcessingRuleRepository;
    private readonly IStartupRegistrationService _startupRegistrationService;
    private readonly LocalLogService _logger;

    public SettingsSaveTransaction(
        SettingsService settingsService,
        IPostProcessingRuleRepository postProcessingRuleRepository,
        IStartupRegistrationService startupRegistrationService,
        LocalLogService logger)
    {
        _settingsService = settingsService;
        _postProcessingRuleRepository = postProcessingRuleRepository;
        _startupRegistrationService = startupRegistrationService;
        _logger = logger;
    }

    public void Execute(
        AppSettings settings,
        PostProcessingConfig postProcessingConfig,
        StartupRegistrationMode? startupRegistrationMode)
    {
        var previousSettings = _settingsService.Current;
        var previousPostProcessingConfig = _postProcessingRuleRepository.Load();
        var startupRegistrationChanged = false;
        StartupRegistrationMode? previousStartupRegistrationMode = null;

        if (startupRegistrationMode is { } requestedStartupRegistrationMode)
        {
            previousStartupRegistrationMode = _startupRegistrationService.GetState().Mode;
            var startupRegistrationResult = _startupRegistrationService.SetMode(
                requestedStartupRegistrationMode);
            if (!startupRegistrationResult.WasSuccessful)
            {
                var message = startupRegistrationResult.Message ?? "更新登录自启动设置失败。";
                if (startupRegistrationResult.WasCanceled)
                {
                    _logger.Warn($"登录自启动设置变更已取消：{message}");
                    throw new InvalidOperationException($"登录自启动设置未更改：{message}");
                }

                _logger.Error($"更新登录自启动设置失败：{message}");
                throw new InvalidOperationException(message);
            }

            startupRegistrationChanged =
                previousStartupRegistrationMode != requestedStartupRegistrationMode;
            _logger.Info($"登录自启动设置已更新：{requestedStartupRegistrationMode}");
        }

        try
        {
            _postProcessingRuleRepository.Save(postProcessingConfig);
            _settingsService.Save(settings);
        }
        catch (Exception saveException)
        {
            var rollbackFailures = new List<string>();
            TryRollback(
                "后处理规则",
                () => _postProcessingRuleRepository.Save(previousPostProcessingConfig),
                rollbackFailures);

            // Save 成功才会替换 Current；未变化时跳过回滚性保存，
            // 避免触发一次同值 SettingsChanged 导致模型/热键被无谓刷新。
            if (!ReferenceEquals(_settingsService.Current, previousSettings))
            {
                TryRollback(
                    "应用设置",
                    () => _settingsService.Save(previousSettings),
                    rollbackFailures);
            }

            if (startupRegistrationChanged && previousStartupRegistrationMode is { } previousMode)
            {
                var rollbackResult = _startupRegistrationService.SetMode(previousMode);
                if (!rollbackResult.WasSuccessful)
                {
                    rollbackFailures.Add(
                        $"登录自启动：{rollbackResult.Message ?? "恢复失败"}");
                }
            }

            var rollbackSuffix = rollbackFailures.Count == 0
                ? "已恢复原设置。"
                : $"部分回滚失败：{string.Join("；", rollbackFailures)}";
            _logger.Error($"保存设置失败，{rollbackSuffix}", saveException);
            throw new InvalidOperationException(
                $"{saveException.Message} {rollbackSuffix}",
                saveException);
        }
    }

    private static void TryRollback(
        string operation,
        Action rollback,
        ICollection<string> failures)
    {
        try
        {
            rollback();
        }
        catch (Exception ex)
        {
            failures.Add($"{operation}：{ex.Message}");
        }
    }
}
