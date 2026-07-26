using System.Text.RegularExpressions;
using HsAsrDictation.Logging;
using HsAsrDictation.PostProcessing.Abstractions;
using HsAsrDictation.PostProcessing.Models;
using HsAsrDictation.PostProcessing.Rules;
using HsAsrDictation.PostProcessing.Validation;

namespace HsAsrDictation.PostProcessing.Engine;

public sealed class PostProcessingRuleFactory : IPostProcessingRuleFactory
{
    private readonly LocalLogService _logger;

    public PostProcessingRuleFactory(LocalLogService logger)
    {
        _logger = logger;
    }

    public IPostProcessingRule? Create(RuleDefinition definition)
    {
        var (ok, error) = RuleValidator.ValidateRule(definition);
        if (!ok)
        {
            _logger.Warn($"后处理规则已跳过：{error}");
            return null;
        }

        try
        {
            return definition.Kind switch
            {
                "exact_replace" => new ExactReplaceRule(
                    definition.Id,
                    definition.Name,
                    definition.Order,
                    definition.IsEnabled,
                    RuleValidator.GetString(definition.Parameters, "find") ?? string.Empty,
                    RuleValidator.GetString(definition.Parameters, "replace") ?? string.Empty,
                    RuleValidator.GetBool(definition.Parameters, "ignoreCase")),
                "regex_replace" => new RegexReplaceRule(
                    definition.Id,
                    definition.Name,
                    definition.Order,
                    definition.IsEnabled,
                    RuleValidator.GetString(definition.Parameters, "pattern") ?? string.Empty,
                    RuleValidator.GetString(definition.Parameters, "replacement") ?? string.Empty,
                    ParseRegexOptions(RuleValidator.GetString(definition.Parameters, "options"))),
                "built_in_transform" => CreateBuiltInRule(definition),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.Warn($"构造后处理规则失败：{definition.Id} | {ex.Message}");
            return null;
        }
    }

    private IPostProcessingRule? CreateBuiltInRule(RuleDefinition definition)
    {
        var transformName = RuleValidator.GetString(definition.Parameters, "transformName") ?? string.Empty;
        return transformName switch
        {
            "trim_whitespace" => new TrimWhitespaceRule(definition.Id, definition.Name, definition.Order, definition.IsEnabled),
            "english_acronym_join" => new EnglishAcronymJoinRule(
                definition.Id,
                definition.Name,
                definition.Order,
                definition.IsEnabled,
                RuleValidator.GetInt(definition.Parameters, "minLetters", 2),
                RuleValidator.GetInt(definition.Parameters, "maxLetters", 8)),
            _ => null
        };
    }

    private static RegexOptions ParseRegexOptions(string? optionsText)
    {
        RegexSafetyValidator.TryParseOptions(optionsText, out var options, out _);
        return options;
    }
}
